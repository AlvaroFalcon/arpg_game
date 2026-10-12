/**
 * A tiny raster painter that produces `draw_grid` rows (one char per pixel, a
 * legend of chars to colours) — the same format EasyPixel's MCP tools take.
 * Used for the higher-definition characters, where drawing every row by hand
 * stops scaling: shapes are painted with primitives, then an outline pass adds
 * the dark contour pixel art needs.
 */
export class Paint {
  readonly g: string[][];

  constructor(readonly w: number, readonly h: number) {
    this.g = Array.from({ length: h }, () => Array.from({ length: w }, () => '.'));
  }

  px(x: number, y: number, c: string): void {
    x = Math.round(x);
    y = Math.round(y);
    if (x >= 0 && y >= 0 && x < this.w && y < this.h) this.g[y][x] = c;
  }

  get(x: number, y: number): string {
    return x >= 0 && y >= 0 && x < this.w && y < this.h ? this.g[y][x] : '.';
  }

  rect(x: number, y: number, w: number, h: number, c: string): void {
    for (let yy = Math.round(y); yy < Math.round(y + h); yy++) for (let xx = Math.round(x); xx < Math.round(x + w); xx++) this.px(xx, yy, c);
  }

  /** Filled ellipse tested at pixel centres; `only` restricts painting to pixels already of those chars. */
  ellipse(cx: number, cy: number, rx: number, ry: number, c: string, only?: string): void {
    for (let y = Math.floor(cy - ry - 1); y <= Math.ceil(cy + ry + 1); y++) {
      for (let x = Math.floor(cx - rx - 1); x <= Math.ceil(cx + rx + 1); x++) {
        const dx = (x + 0.5 - cx) / rx;
        const dy = (y + 0.5 - cy) / ry;
        if (dx * dx + dy * dy <= 1 && (!only || only.includes(this.get(x, y)))) this.px(x, y, c);
      }
    }
  }

  /** Filled polygon (even-odd rule at pixel centres). */
  poly(pts: [number, number][], c: string, only?: string): void {
    const ys = pts.map((p) => p[1]);
    const xs = pts.map((p) => p[0]);
    for (let y = Math.floor(Math.min(...ys)); y <= Math.ceil(Math.max(...ys)); y++) {
      for (let x = Math.floor(Math.min(...xs)); x <= Math.ceil(Math.max(...xs)); x++) {
        const px = x + 0.5;
        const py = y + 0.5;
        let inside = false;
        for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) {
          const [xi, yi] = pts[i];
          const [xj, yj] = pts[j];
          if (yi > py !== yj > py && px < ((xj - xi) * (py - yi)) / (yj - yi) + xi) inside = !inside;
        }
        if (inside && (!only || only.includes(this.get(x, y)))) this.px(x, y, c);
      }
    }
  }

  line(x0: number, y0: number, x1: number, y1: number, c: string, thick = 1): void {
    const steps = Math.max(Math.abs(x1 - x0), Math.abs(y1 - y0), 1);
    for (let i = 0; i <= steps; i++) {
      const x = x0 + ((x1 - x0) * i) / steps;
      const y = y0 + ((y1 - y0) * i) / steps;
      if (thick <= 1) this.px(x, y, c);
      else this.rect(Math.round(x - (thick - 1) / 2), Math.round(y - (thick - 1) / 2), thick, thick, c);
    }
  }

  /** Stamps rows of chars at (x, y); '.' is transparent. */
  stamp(x: number, y: number, rows: string[]): void {
    rows.forEach((r, dy) => [...r].forEach((c, dx) => c !== '.' && this.px(x + dx, y + dy, c)));
  }

  /** Replaces chars inside a region (shading passes). */
  recolorWhere(test: (x: number, y: number, c: string) => boolean, c: string): void {
    for (let y = 0; y < this.h; y++) for (let x = 0; x < this.w; x++) if (test(x, y, this.g[y][x])) this.g[y][x] = c;
  }

  /** Dark contour: every empty pixel touching a painted one (4-neighbour) becomes `c`. */
  outline(c = 'k'): void {
    const marks: [number, number][] = [];
    for (let y = 0; y < this.h; y++) {
      for (let x = 0; x < this.w; x++) {
        if (this.g[y][x] !== '.') continue;
        const n = [this.get(x - 1, y), this.get(x + 1, y), this.get(x, y - 1), this.get(x, y + 1)];
        if (n.some((v) => v !== '.' && v !== c)) marks.push([x, y]);
      }
    }
    for (const [x, y] of marks) this.g[y][x] = c;
  }

  rows(): string[] {
    return this.g.map((r) => r.join(''));
  }
}
