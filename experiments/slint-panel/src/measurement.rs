//! Stage 0 measurement, the same fields as the WinUI 3 version so one script reads both.
//! Only active with `--measure`; writes a local JSON file, never sends anything.

use std::fmt::Write as _;
use std::path::PathBuf;
use std::time::Instant;

use crate::system::since_process_start;

const MAX_FRAMES: usize = 20_000;

pub struct MeasurementLog {
    path: PathBuf,
    cold_start_ms: Option<f64>,
    phases_ms: Vec<(&'static str, f64)>,
    panel_open_ms: Vec<f64>,
    frame_intervals_ms: Vec<f64>,
    panel_requested_at: Option<Instant>,
    last_frame_at: Option<Instant>,
    particle_count: usize,
}

impl MeasurementLog {
    pub fn new(path: PathBuf) -> Self {
        Self {
            path,
            cold_start_ms: None,
            phases_ms: Vec::new(),
            panel_open_ms: Vec::new(),
            frame_intervals_ms: Vec::new(),
            panel_requested_at: None,
            last_frame_at: None,
            particle_count: 0,
        }
    }

    pub fn mark_phase(&mut self, name: &'static str) {
        self.phases_ms.push((name, millis(since_process_start().as_secs_f64())));
    }

    pub fn mark_panel_requested(&mut self) {
        self.panel_requested_at = Some(Instant::now());
        self.last_frame_at = None;
    }

    pub fn mark_panel_hidden(&mut self) {
        self.last_frame_at = None;
    }

    pub fn record_frame(&mut self, now: Instant, particle_count: usize) {
        if self.cold_start_ms.is_none() {
            self.cold_start_ms = Some(millis(since_process_start().as_secs_f64()));
        }
        if let Some(requested) = self.panel_requested_at.take() {
            self.panel_open_ms.push(millis((now - requested).as_secs_f64()));
        } else if let Some(last) = self.last_frame_at
            && self.frame_intervals_ms.len() < MAX_FRAMES
        {
            self.frame_intervals_ms.push(millis((now - last).as_secs_f64()));
        }
        self.last_frame_at = Some(now);
        self.particle_count = particle_count;
    }

    pub fn save(&self) -> std::io::Result<()> {
        let mut json = String::from("{\n");
        let cold = self.cold_start_ms.map_or("null".to_owned(), |ms| ms.to_string());
        let _ = writeln!(json, "  \"ColdStartToFirstFrameMs\": {cold},");
        let phases: Vec<String> = self.phases_ms.iter().map(|(name, ms)| format!("\"{name}\": {ms}")).collect();
        let _ = writeln!(json, "  \"PhasesMs\": {{ {} }},", phases.join(", "));
        let _ = writeln!(json, "  \"PanelOpenToFirstFrameMs\": {},", list(&self.panel_open_ms));
        let _ = writeln!(json, "  \"LastParticleCount\": {},", self.particle_count);
        let _ = writeln!(json, "  \"FrameIntervalsMs\": {}", list(&self.frame_intervals_ms));
        json.push_str("}\n");
        std::fs::write(&self.path, json)
    }
}

fn millis(seconds: f64) -> f64 {
    seconds * 1000.0
}

fn list(values: &[f64]) -> String {
    let items: Vec<String> = values.iter().map(f64::to_string).collect();
    format!("[{}]", items.join(", "))
}
