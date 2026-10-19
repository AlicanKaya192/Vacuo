//! The VOID scene, drawn on the CPU into an RGBA buffer because Slint has no custom GPU drawing.
//! Same simulation as the WinUI 3 version: an accretion disk seen at an angle, its particles
//! spiralling into a dark event horizon. Radii are normalised (1 = disk edge).

use std::f32::consts::TAU;
use std::time::Duration;

use slint::{Rgba8Pixel, SharedPixelBuffer};

const TILT: f32 = 0.42;
const HORIZON: f32 = 0.2;
const INNER_SPAWN: f32 = 0.3;
const LIGHT_PARTICLE_SHARE: f32 = 0.3;
const MINIMUM_PARTICLES: usize = 200;
const SLOW_FRAME_MS: f64 = 22.0;
const TRIM_FACTOR: f64 = 0.9;
const RING_WIDTH: f32 = 1.5;

const VOID: [u8; 3] = [0x0A, 0x08, 0x11];
const ACCENT: [u8; 3] = [0x9D, 0x7C, 0xFF];
const ACCENT_LIGHT: [u8; 3] = [0xD4, 0xC6, 0xFF];

#[derive(Clone, Copy)]
struct Particle {
    radius: f32,
    angle: f32,
    pull: f32,
    size: f32,
    is_light: bool,
}

pub struct VoidField {
    particles: Vec<Particle>,
    count: usize,
    average_frame_ms: f64,
    random: XorShift,
}

impl VoidField {
    pub fn new(particle_count: usize) -> Self {
        let mut random = XorShift::seeded();
        let particles = (0..particle_count)
            .map(|_| {
                // Start outside the horizon; a still frame shows this layout as is.
                let radius = HORIZON + random.next() * (1.0 - HORIZON);
                spawn(&mut random, radius)
            })
            .collect();
        Self { particles, count: particle_count, average_frame_ms: 16.0, random }
    }

    pub fn count(&self) -> usize {
        self.count
    }

    pub fn advance(&mut self, elapsed: Duration) {
        let dt = elapsed.as_secs_f32().min(0.05);
        self.adapt_to_frame_time(elapsed.as_secs_f64() * 1000.0);
        for particle in &mut self.particles[..self.count] {
            particle.angle += dt * 0.55 / (particle.radius + 0.04).sqrt();
            particle.radius -= dt * 0.06 * particle.pull * (1.2 - particle.radius.min(1.0));
            if particle.radius < HORIZON {
                let radius = INNER_SPAWN + self.random.next() * (1.0 - INNER_SPAWN);
                *particle = spawn(&mut self.random, radius);
            }
        }
    }

    /// Draws one frame; `scale` turns the logical particle sizes into pixels.
    pub fn render(&self, width: u32, height: u32, scale: f32) -> SharedPixelBuffer<Rgba8Pixel> {
        let mut buffer = SharedPixelBuffer::<Rgba8Pixel>::new(width, height);
        let mut canvas = Canvas { pixels: buffer.make_mut_slice(), width, height };
        canvas.clear(VOID);

        let center = (width as f32 / 2.0, height as f32 / 2.0);
        let disk_radius = (width as f32 * 0.47).min(height as f32 * 0.45 / TILT);
        // The disk is drawn in two halves so the horizon hides the far side: a cheap 3D effect.
        self.draw_particles(&mut canvas, center, disk_radius, scale, false);
        canvas.fill_circle(center, disk_radius * HORIZON, VOID);
        canvas.stroke_circle(center, disk_radius * HORIZON, RING_WIDTH * scale, ACCENT);
        self.draw_particles(&mut canvas, center, disk_radius, scale, true);
        buffer
    }

    fn draw_particles(&self, canvas: &mut Canvas, center: (f32, f32), disk_radius: f32, scale: f32, front: bool) {
        for particle in &self.particles[..self.count] {
            let sin = particle.angle.sin();
            if (sin >= 0.0) != front {
                continue;
            }
            let x = center.0 + particle.angle.cos() * particle.radius * disk_radius;
            let y = center.1 + sin * particle.radius * disk_radius * TILT;
            let nearness = 1.0 - ((particle.radius - HORIZON) / (1.0 - HORIZON)).min(1.0);
            let color = if particle.is_light { ACCENT_LIGHT } else { ACCENT };
            canvas.blend_square(x, y, particle.size * scale, color, 0.18 + 0.8 * nearness);
        }
    }

    // Fewer particles when frames get slow: the effect must never cost the user responsiveness.
    fn adapt_to_frame_time(&mut self, frame_ms: f64) {
        self.average_frame_ms = self.average_frame_ms * 0.95 + frame_ms * 0.05;
        if self.average_frame_ms > SLOW_FRAME_MS && self.count > MINIMUM_PARTICLES {
            self.count = MINIMUM_PARTICLES.max((self.count as f64 * TRIM_FACTOR) as usize);
            self.average_frame_ms = 16.0;
        }
    }
}

fn spawn(random: &mut XorShift, radius: f32) -> Particle {
    Particle {
        radius,
        angle: random.next() * TAU,
        pull: 0.6 + random.next() * 0.8,
        size: if random.next() < 0.12 { 1.8 } else { 1.1 },
        is_light: random.next() < LIGHT_PARTICLE_SHARE,
    }
}

struct Canvas<'a> {
    pixels: &'a mut [Rgba8Pixel],
    width: u32,
    height: u32,
}

impl Canvas<'_> {
    fn clear(&mut self, [r, g, b]: [u8; 3]) {
        self.pixels.fill(Rgba8Pixel { r, g, b, a: 255 });
    }

    fn blend_square(&mut self, x: f32, y: f32, size: f32, color: [u8; 3], alpha: f32) {
        let (x0, y0) = (x.floor() as i64, y.floor() as i64);
        let side = size.ceil().max(1.0) as i64;
        for py in y0..y0 + side {
            for px in x0..x0 + side {
                self.blend(px, py, color, alpha);
            }
        }
    }

    fn fill_circle(&mut self, center: (f32, f32), radius: f32, color: [u8; 3]) {
        self.for_each_in_box(center, radius + 1.0, |canvas, px, py, distance| {
            if distance <= radius {
                canvas.blend(px, py, color, 1.0);
            }
        });
    }

    fn stroke_circle(&mut self, center: (f32, f32), radius: f32, width: f32, color: [u8; 3]) {
        self.for_each_in_box(center, radius + width, |canvas, px, py, distance| {
            let coverage = (width / 2.0 + 0.5 - (distance - radius).abs()).clamp(0.0, 1.0);
            if coverage > 0.0 {
                canvas.blend(px, py, color, 0.85 * coverage);
            }
        });
    }

    fn for_each_in_box(&mut self, center: (f32, f32), reach: f32, mut visit: impl FnMut(&mut Self, i64, i64, f32)) {
        let (left, right) = ((center.0 - reach).floor() as i64, (center.0 + reach).ceil() as i64);
        let (top, bottom) = ((center.1 - reach).floor() as i64, (center.1 + reach).ceil() as i64);
        for py in top..=bottom {
            for px in left..=right {
                let distance = ((px as f32 + 0.5 - center.0).powi(2) + (py as f32 + 0.5 - center.1).powi(2)).sqrt();
                visit(self, px, py, distance);
            }
        }
    }

    fn blend(&mut self, x: i64, y: i64, [r, g, b]: [u8; 3], alpha: f32) {
        if x < 0 || y < 0 || x >= i64::from(self.width) || y >= i64::from(self.height) {
            return;
        }
        let pixel = &mut self.pixels[(y as usize) * self.width as usize + x as usize];
        let mix = |source: u8, target: u8| (f32::from(source) * alpha + f32::from(target) * (1.0 - alpha)) as u8;
        pixel.r = mix(r, pixel.r);
        pixel.g = mix(g, pixel.g);
        pixel.b = mix(b, pixel.b);
    }
}

/// Small deterministic random source; the scene needs no cryptographic quality and no extra crate.
struct XorShift(u64);

impl XorShift {
    fn seeded() -> Self {
        let nanos = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map_or(0x9E37_79B9_7F4A_7C15, |time| time.as_nanos() as u64);
        Self(nanos | 1)
    }

    /// A number in [0, 1).
    fn next(&mut self) -> f32 {
        self.0 ^= self.0 << 13;
        self.0 ^= self.0 >> 7;
        self.0 ^= self.0 << 17;
        (self.0 >> 40) as f32 / (1u64 << 24) as f32
    }
}
