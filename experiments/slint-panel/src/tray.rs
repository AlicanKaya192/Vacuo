//! The Vacuo logo in the notification area, on its own thread with a hidden window,
//! the same way Sightline does it. Clicks are handed to the Slint thread.

use std::cell::RefCell;
use std::ffi::c_void;
use std::path::Path;
use std::ptr;
use std::sync::Arc;
use std::sync::atomic::{AtomicPtr, Ordering};
use std::thread::{self, JoinHandle};

use windows_sys::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM};
use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
use windows_sys::Win32::UI::Shell::{NIF_ICON, NIF_MESSAGE, NIF_TIP, NIM_ADD, NIM_DELETE, NOTIFYICONDATAW, Shell_NotifyIconW};
use windows_sys::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyIcon, DestroyWindow, DispatchMessageW, GetMessageW, GetSystemMetrics,
    HICON, IMAGE_ICON, LR_LOADFROMFILE, LoadImageW, MSG, PostMessageW, PostQuitMessage, RegisterClassW,
    SM_CXSMICON, SM_CYSMICON, TranslateMessage, WM_APP, WM_CLOSE, WM_LBUTTONUP, WM_RBUTTONUP, WNDCLASSW,
};

use crate::system::wide;

const ICON_MESSAGE: u32 = WM_APP + 1;
const ICON_ID: u32 = 1;

thread_local! {
    static ON_CLICK: RefCell<Option<Box<dyn Fn()>>> = RefCell::new(None);
    static ICON: RefCell<Option<NOTIFYICONDATAW>> = const { RefCell::new(None) };
}

pub struct Tray {
    window: Arc<AtomicPtr<c_void>>,
    thread: Option<JoinHandle<()>>,
}

impl Tray {
    pub fn start(icon_path: &Path, tooltip: &str, on_click: impl Fn() + Send + 'static) -> Self {
        let window = Arc::new(AtomicPtr::new(ptr::null_mut()));
        let shared = Arc::clone(&window);
        let icon_path = wide(&icon_path.to_string_lossy());
        let tooltip = tooltip.to_owned();
        let thread = thread::spawn(move || {
            ON_CLICK.set(Some(Box::new(on_click)));
            match create_window_with_icon(&icon_path, &tooltip) {
                Ok(hwnd) => {
                    shared.store(hwnd, Ordering::SeqCst);
                    pump_messages();
                }
                Err(error) => eprintln!("tray icon unavailable: {error}"),
            }
        });
        Self { window, thread: Some(thread) }
    }

    /// Removes the icon and waits for the tray thread to finish.
    pub fn close(mut self) {
        let hwnd = self.window.load(Ordering::SeqCst);
        if hwnd.is_null() {
            return;
        }
        // SAFETY: posting to a window owned by the tray thread, which handles WM_CLOSE.
        unsafe { PostMessageW(hwnd, WM_CLOSE, 0, 0) };
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

fn create_window_with_icon(icon_path: &[u16], tooltip: &str) -> std::io::Result<HWND> {
    let class = wide("VacuoSlintTray");
    // SAFETY: `class` outlives the calls; `window_proc` matches WNDPROC.
    let window = unsafe {
        let instance = GetModuleHandleW(ptr::null());
        let window_class = WNDCLASSW {
            lpfnWndProc: Some(window_proc),
            hInstance: instance,
            lpszClassName: class.as_ptr(),
            ..Default::default()
        };
        if RegisterClassW(&window_class) == 0 {
            return Err(std::io::Error::last_os_error());
        }
        CreateWindowExW(0, class.as_ptr(), ptr::null(), 0, 0, 0, 0, 0, ptr::null_mut(), ptr::null_mut(), instance, ptr::null())
    };
    if window.is_null() {
        return Err(std::io::Error::last_os_error());
    }

    // SAFETY: loads an icon file into a new handle that WM_CLOSE destroys.
    let icon = unsafe {
        LoadImageW(ptr::null_mut(), icon_path.as_ptr(), IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE)
    } as HICON;
    if icon.is_null() {
        return Err(std::io::Error::last_os_error());
    }

    let mut data = NOTIFYICONDATAW {
        cbSize: size_of::<NOTIFYICONDATAW>() as u32,
        hWnd: window,
        uID: ICON_ID,
        uFlags: NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage: ICON_MESSAGE,
        hIcon: icon,
        ..Default::default()
    };
    for (slot, unit) in data.szTip.iter_mut().zip(tooltip.encode_utf16().take(127)) {
        *slot = unit;
    }
    // SAFETY: `data` describes a live window and icon.
    if unsafe { Shell_NotifyIconW(NIM_ADD, &data) } == 0 {
        return Err(std::io::Error::last_os_error());
    }
    ICON.set(Some(data));
    Ok(window)
}

fn pump_messages() {
    let mut message = MSG::default();
    // SAFETY: `message` is a writable MSG passed back to the system unchanged.
    while unsafe { GetMessageW(&mut message, ptr::null_mut(), 0, 0) } > 0 {
        unsafe {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
}

unsafe extern "system" fn window_proc(window: HWND, message: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    match message {
        ICON_MESSAGE => {
            let mouse = lparam as u32;
            if mouse == WM_LBUTTONUP || mouse == WM_RBUTTONUP {
                ON_CLICK.with_borrow(|on_click| on_click.as_ref().map(|click| click()));
            }
            0
        }
        WM_CLOSE => {
            if let Some(data) = ICON.take() {
                // SAFETY: removes the icon this thread added and frees its handle.
                unsafe {
                    Shell_NotifyIconW(NIM_DELETE, &data);
                    DestroyIcon(data.hIcon);
                }
            }
            // SAFETY: the window belongs to this thread.
            unsafe {
                DestroyWindow(window);
                PostQuitMessage(0);
            }
            0
        }
        // SAFETY: default handling for every other message.
        _ => unsafe { DefWindowProcW(window, message, wparam, lparam) },
    }
}
