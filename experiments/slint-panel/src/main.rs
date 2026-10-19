//! Stage 0, Deneme B: the Vacuo tray panel in Rust + Slint, measured against the WinUI 3 version.
//! Switches: `--lang en-US`, `--measure <file.json>`, `--open`, `--script idle|coldstart|full`,
//! `--reduced-motion`.

#![windows_subsystem = "windows"]

mod measurement;
mod system;
mod tray;
mod void_field;

use std::cell::RefCell;
use std::path::{Path, PathBuf};
use std::rc::Rc;
use std::time::{Duration, Instant};

use slint::{ComponentHandle, Image, PhysicalPosition, RenderingState, Timer};

use measurement::MeasurementLog;
use tray::Tray;
use void_field::VoidField;

slint::include_modules!();

const PARTICLE_COUNT: usize = 2000;
const PANEL_WIDTH: f32 = 360.0;
const PANEL_PADDING: f32 = 14.0;
const VOID_HEIGHT: f32 = 200.0;
const SCREEN_MARGIN: f32 = 12.0;

struct Options {
    language: Option<String>,
    measurement_path: Option<PathBuf>,
    open_at_start: bool,
    script: Option<String>,
    reduced_motion: bool,
}

impl Options {
    fn parse(mut args: impl Iterator<Item = String>) -> Self {
        let mut options =
            Self { language: None, measurement_path: None, open_at_start: false, script: None, reduced_motion: false };
        while let Some(arg) = args.next() {
            match arg.as_str() {
                "--lang" => options.language = args.next(),
                "--measure" => options.measurement_path = args.next().map(PathBuf::from),
                "--open" => options.open_at_start = true,
                "--script" => options.script = args.next(),
                "--reduced-motion" => options.reduced_motion = true,
                _ => {}
            }
        }
        options
    }
}

struct Panel {
    ui: PanelWindow,
    field: VoidField,
    measurement: Option<MeasurementLog>,
    animate: bool,
    is_open: bool,
    placed: bool,
    last_advance: Instant,
}

thread_local! {
    static PANEL: RefCell<Option<Panel>> = const { RefCell::new(None) };
}

fn with_panel(action: impl FnOnce(&mut Panel)) {
    PANEL.with_borrow_mut(|panel| {
        if let Some(panel) = panel {
            action(panel);
        }
    });
}

impl Panel {
    fn toggle(&mut self) {
        if self.is_open { self.hide() } else { self.open() }
    }

    fn open(&mut self) {
        if let Some(measurement) = &mut self.measurement {
            measurement.mark_panel_requested();
        }
        let _ = self.ui.show();
        self.place_above_tray();
        self.placed = false;
        self.is_open = true;
        self.last_advance = Instant::now();
        self.ui.set_cards_shown(true);
        self.draw_frame();
    }

    fn hide(&mut self) {
        let _ = self.ui.hide();
        self.ui.set_cards_shown(false);
        self.is_open = false;
        if let Some(measurement) = &mut self.measurement {
            measurement.mark_panel_hidden();
        }
    }

    /// Uses the window's real size: until the first frame, Slint may still report scale 1,
    /// which put the panel past the right edge and over the clock on a scaled screen.
    fn place_above_tray(&self) {
        let area = system::work_area();
        let size = self.ui.window().size();
        let margin = (SCREEN_MARGIN * self.ui.window().scale_factor()) as i32;
        let (width, height) = (size.width as i32, size.height as i32);
        self.ui.window().set_position(PhysicalPosition::new(area.right - width - margin, area.bottom - height - margin));
    }

    /// Advances the scene and hands Slint a new image; setting it schedules the next render.
    fn draw_frame(&mut self) {
        if self.animate {
            let now = Instant::now();
            self.field.advance(now - self.last_advance);
            self.last_advance = now;
        }
        let scale = self.ui.window().scale_factor();
        let width = ((PANEL_WIDTH - 2.0 * PANEL_PADDING) * scale) as u32;
        let height = (VOID_HEIGHT * scale) as u32;
        let buffer = self.field.render(width, height, scale);
        self.ui.set_void_image(Image::from_rgba8(buffer));
    }

    fn after_render(&mut self, drawn_at: Instant) {
        if !self.is_open {
            return;
        }
        if let Some(measurement) = &mut self.measurement {
            measurement.record_frame(drawn_at, self.field.count());
        }
        if !self.placed {
            self.place_above_tray();
            self.placed = true;
        }
        if self.animate {
            // Drawing again right after a render paces the animation by the renderer.
            self.draw_frame();
        }
    }
}

fn main() -> Result<(), slint::PlatformError> {
    let options = Options::parse(std::env::args().skip(1));
    let language = options.language.clone().unwrap_or_else(system::windows_language);
    let turkish = language.to_ascii_lowercase().starts_with("tr");

    let ui = PanelWindow::new()?;
    // Creating the window lets Slint pick the Windows language, so the choice comes after it.
    if let Err(error) = slint::select_bundled_translation(if turkish { "tr" } else { "en" }) {
        eprintln!("could not select the translation: {error}");
    }
    if let Some(drive) = system::system_drive() {
        ui.set_drive(drive.root.into());
        ui.set_free_space(system::format_bytes(drive.free_bytes, turkish).into());
        ui.set_total_space(system::format_bytes(drive.total_bytes, turkish).into());
    }
    ui.on_quit(|| {
        let _ = slint::quit_event_loop();
    });
    ui.window()
        .set_rendering_notifier(|state, _| {
            if matches!(state, RenderingState::AfterRendering) {
                // A render can happen while the panel is busy (for example inside `show`),
                // so the frame is handled right after, with the time it was really drawn.
                let drawn_at = Instant::now();
                Timer::single_shot(Duration::ZERO, move || with_panel(|panel| panel.after_render(drawn_at)));
            }
        })
        .map_err(|error| slint::PlatformError::Other(error.to_string()))?;

    PANEL.set(Some(Panel {
        ui,
        field: VoidField::new(PARTICLE_COUNT),
        measurement: options.measurement_path.clone().map(MeasurementLog::new),
        animate: !options.reduced_motion,
        is_open: false,
        placed: false,
        last_advance: Instant::now(),
    }));

    let icon = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../src/Vacuo.App/Assets/vacuo.ico");
    let tray = Tray::start(&icon, "Vacuo", || {
        let _ = slint::invoke_from_event_loop(|| with_panel(Panel::toggle));
    });

    if options.open_at_start {
        with_panel(Panel::open);
    }
    if let Some(script) = &options.script {
        run_steps(Rc::from(script_steps(script)), 0);
    }

    slint::run_event_loop_until_quit()?;

    PANEL.with_borrow(|panel| {
        if let Some(measurement) = panel.as_ref().and_then(|panel| panel.measurement.as_ref()) {
            let _ = measurement.save();
        }
    });
    tray.close();
    Ok(())
}

#[derive(Clone, Copy)]
enum Action {
    Toggle,
    Phase(&'static str),
    Quit,
}

#[derive(Clone, Copy)]
struct Step {
    after: Duration,
    action: Action,
}

const fn step(after_secs: u64, action: Action) -> Step {
    Step { after: Duration::from_secs(after_secs), action }
}

/// The same scenarios as the WinUI 3 measurement script.
fn script_steps(mode: &str) -> Vec<Step> {
    match mode {
        "idle" => vec![step(0, Action::Phase("idle-start")), step(70, Action::Phase("idle-end")), step(0, Action::Quit)],
        "coldstart" => vec![step(0, Action::Toggle), step(3, Action::Quit)],
        "full" => {
            let mut steps = vec![
                step(0, Action::Toggle),
                step(0, Action::Phase("open-start")),
                step(30, Action::Phase("open-end")),
                step(0, Action::Toggle),
                step(0, Action::Phase("closed-start")),
                step(60, Action::Phase("closed-end")),
                step(0, Action::Phase("cycles-start")),
            ];
            for _ in 0..5 {
                steps.push(step(0, Action::Toggle));
                steps.push(step(2, Action::Toggle));
                steps.push(step(2, Action::Phase("cycle")));
            }
            steps.push(step(0, Action::Phase("cycles-end")));
            steps.push(step(0, Action::Quit));
            steps
        }
        unknown => {
            eprintln!("unknown measurement script '{unknown}'");
            vec![step(0, Action::Quit)]
        }
    }
}

fn run_steps(steps: Rc<[Step]>, index: usize) {
    let Some(&Step { after, action }) = steps.get(index) else {
        return;
    };
    Timer::single_shot(after, move || {
        match action {
            Action::Toggle => with_panel(Panel::toggle),
            Action::Phase(name) => with_panel(|panel| {
                if let Some(measurement) = &mut panel.measurement {
                    measurement.mark_phase(name);
                }
            }),
            Action::Quit => {
                let _ = slint::quit_event_loop();
            }
        }
        run_steps(steps, index + 1);
    });
}
