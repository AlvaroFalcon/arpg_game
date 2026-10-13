// A tiny raster for procedurally drawn sprites ('.' is transparent).

/** A character grid with drawing helpers ('.' is transparent). */
export class Pix {
  g: string[][];
  constructor(public w: number, public h: number, fill = '.') {
    this.g = Array.from({ length: h }, () => Array(w).fill(fill));
  }
  set(x: number, y: number, c: string): this {
    if (x >= 0 && y >= 0 && x < this.w && y < this.h) this.g[y][x] = c;
    return this;
  }
  rect(x: number, y: number, w: number, h: number, c: string): this {
    for (let j = y; j < y + h; j++) for (let i = x; i < x + w; i++) this.set(i, j, c);
    return this;
  }
  box(x: number, y: number, w: number, h: number, c: string): this {
    for (let i = x; i < x + w; i++) this.set(i, y, c).set(i, y + h - 1, c);
    for (let j = y; j < y + h; j++) this.set(x, j, c).set(x + w - 1, j, c);
    return this;
  }
  disc(cx: number, cy: number, rx: number, ry: number, c: string): this {
    for (let y = Math.floor(cy - ry); y <= Math.ceil(cy + ry); y++) {
      for (let x = Math.floor(cx - rx); x <= Math.ceil(cx + rx); x++) {
        if (((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1) this.set(x, y, c);
      }
    }
    return this;
  }
  /** Outlines every filled pixel that touches transparency with `c`. */
  outline(c: string): this {
    const src = this.g.map((r) => [...r]);
    for (let y = 0; y < this.h; y++) {
      for (let x = 0; x < this.w; x++) {
        if (src[y][x] !== '.') continue;
        const n = [[1, 0], [-1, 0], [0, 1], [0, -1]].some(([dx, dy]) => src[y + dy]?.[x + dx] && src[y + dy][x + dx] !== '.');
        if (n) this.g[y][x] = c;
      }
    }
    return this;
  }
  line(x0: number, y0: number, x1: number, y1: number, c: string, w = 1): this {
    const steps = Math.max(Math.abs(x1 - x0), Math.abs(y1 - y0), 1);
    for (let i = 0; i <= steps; i++) {
      const x = Math.round(x0 + ((x1 - x0) * i) / steps);
      const y = Math.round(y0 + ((y1 - y0) * i) / steps);
      for (let dy = 0; dy < w; dy++) for (let dx = 0; dx < w; dx++) this.set(x + dx, y + dy, c);
    }
    return this;
  }
  /** Fills a polygon (pixel centres inside it). */
  poly(points: [number, number][], c: string): this {
    for (let y = 0; y < this.h; y++) {
      for (let x = 0; x < this.w; x++) {
        let inside = false;
        for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
          const [xi, yi] = points[i];
          const [xj, yj] = points[j];
          if (yi > y + 0.5 !== yj > y + 0.5 && x + 0.5 < ((xj - xi) * (y + 0.5 - yi)) / (yj - yi) + xi) inside = !inside;
        }
        if (inside) this.set(x, y, c);
      }
    }
    return this;
  }
  ring(cx: number, cy: number, r: number, thickness: number, c: string): this {
    for (let y = 0; y < this.h; y++) {
      for (let x = 0; x < this.w; x++) {
        const d = Math.hypot(x + 0.5 - cx, y + 0.5 - cy);
        if (d <= r && d > r - thickness) this.set(x, y, c);
      }
    }
    return this;
  }
  /** Replaces colour `from` with `to` where `test(x, y)` holds (simple shading). */
  shade(from: string, to: string, test: (x: number, y: number) => boolean): this {
    for (let y = 0; y < this.h; y++) for (let x = 0; x < this.w; x++) if (this.g[y][x] === from && test(x, y)) this.g[y][x] = to;
    return this;
  }
  rows(): string[] {
    return this.g.map((r) => r.join(''));
  }
}

