//! The few facts this experiment asks Windows for.

use std::ptr;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use windows_sys::Win32::Foundation::{FILETIME, RECT};
use windows_sys::Win32::Globalization::{GetUserPreferredUILanguages, MUI_LANGUAGE_NAME};
use windows_sys::Win32::Storage::FileSystem::GetDiskFreeSpaceExW;
use windows_sys::Win32::System::Threading::{GetCurrentProcess, GetProcessTimes};
use windows_sys::Win32::UI::WindowsAndMessaging::{SPI_GETWORKAREA, SystemParametersInfoW};

/// 100 ns intervals between 1601-01-01 (FILETIME) and 1970-01-01 (Unix).
const FILETIME_UNIX_OFFSET: u64 = 116_444_736_000_000_000;

pub struct DriveSpace {
    pub root: String,
    pub free_bytes: u64,
    pub total_bytes: u64,
}

pub fn system_drive() -> Option<DriveSpace> {
    let root = std::env::var("SystemDrive").unwrap_or_else(|_| "C:".to_owned());
    let path = wide(&format!("{root}\\"));
    let (mut free, mut total) = (0u64, 0u64);
    // SAFETY: `path` is NUL-terminated and outlives the call; the outputs are plain integers.
    let ok = unsafe { GetDiskFreeSpaceExW(path.as_ptr(), &mut free, &mut total, ptr::null_mut()) };
    (ok != 0).then_some(DriveSpace { root, free_bytes: free, total_bytes: total })
}

/// The primary screen without the taskbar, in physical pixels.
pub fn work_area() -> RECT {
    let mut area = RECT { left: 0, top: 0, right: 0, bottom: 0 };
    // SAFETY: SPI_GETWORKAREA writes one RECT into `area`.
    unsafe { SystemParametersInfoW(SPI_GETWORKAREA, 0, (&raw mut area).cast(), 0) };
    area
}

/// The user's first display language, such as "tr-TR".
pub fn windows_language() -> String {
    let (mut count, mut size) = (0, 0);
    // SAFETY: the first call reports the buffer size the second call fills.
    unsafe {
        if GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, &mut count, ptr::null_mut(), &mut size) == 0 {
            return String::new();
        }
        let mut buffer = vec![0u16; size as usize];
        if GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, &mut count, buffer.as_mut_ptr(), &mut size) == 0 {
            return String::new();
        }
        let first = buffer.split(|&unit| unit == 0).next().unwrap_or_default();
        String::from_utf16_lossy(first)
    }
}

/// Time since Windows created this process, the same start point the WinUI version uses.
pub fn since_process_start() -> Duration {
    let zero = FILETIME { dwLowDateTime: 0, dwHighDateTime: 0 };
    let (mut created, mut exited, mut kernel, mut user) = (zero, zero, zero, zero);
    // SAFETY: the pseudo handle of the current process is always valid.
    unsafe { GetProcessTimes(GetCurrentProcess(), &mut created, &mut exited, &mut kernel, &mut user) };
    let ticks = (u64::from(created.dwHighDateTime) << 32) | u64::from(created.dwLowDateTime);
    let start = UNIX_EPOCH + Duration::from_nanos(ticks.saturating_sub(FILETIME_UNIX_OFFSET) * 100);
    SystemTime::now().duration_since(start).unwrap_or_default()
}

pub fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(std::iter::once(0)).collect()
}

/// Binary units with one decimal, like Windows Explorer; Turkish uses a decimal comma.
pub fn format_bytes(bytes: u64, decimal_comma: bool) -> String {
    const UNITS: [&str; 4] = ["KB", "MB", "GB", "TB"];
    if bytes < 1024 {
        return format!("{bytes} B");
    }
    let mut value = bytes as f64;
    let mut unit = 0;
    while value >= 1024.0 && unit < UNITS.len() {
        value /= 1024.0;
        unit += 1;
    }
    let number = format!("{value:.1}");
    let number = if decimal_comma { number.replace('.', ",") } else { number };
    format!("{number} {}", UNITS[unit - 1])
}

#[cfg(test)]
mod tests {
    use super::format_bytes;

    #[test]
    fn small_sizes_stay_in_bytes() {
        assert_eq!(format_bytes(512, false), "512 B");
    }

    #[test]
    fn turkish_uses_a_decimal_comma() {
        assert_eq!(format_bytes((4.2 * 1024.0 * 1024.0 * 1024.0) as u64, true), "4,2 GB");
    }

    #[test]
    fn english_uses_a_decimal_point() {
        assert_eq!(format_bytes(1536, false), "1.5 KB");
    }
}
