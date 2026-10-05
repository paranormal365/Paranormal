import sys, subprocess, time
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import *
from scenes import *

S = {}
def scenes():
    if not S:
        S[1] = Scene1(); S[2] = Scene2(); S[3] = Scene3()
        S[4] = Scene4(S[3]); S[5] = Scene5(S[3]); S[6] = Scene6()
    return S

def render_scene(n, gt):
    s, c = new_surface(); setc(c, BG); c.paint()
    local = gt - {1: T1, 2: T2, 3: T3, 4: T4, 5: T5, 6: T6}[n]
    scenes()[n].draw(c, local, gt)
    return s

def frame(gt):
    sc = scenes()
    out, c = new_surface(); setc(c, (0, 0, 0, 1)); c.paint()
    def put(surf, a=1.0, dx=0, dy=0):
        c.set_source_surface(surf, dx, dy); c.paint_with_alpha(a)

    if gt < T2 - .3:
        put(render_scene(1, gt))
    elif gt < T2:
        put(render_scene(1, gt)); put(render_scene(2, gt), ease(seg(gt, T2 - .3, T2)))
    elif gt < T3:
        put(render_scene(2, gt))
    elif gt < T4 - .35:
        put(render_scene(3, gt))
        fa = 1 - ease(seg(gt, T3, T3 + .4))
        if fa > 0: setc(c, hx('#FFF1D0', fa)); c.paint()
    elif gt < T4:
        put(render_scene(3, gt)); put(render_scene(4, gt), ease(seg(gt, T4 - .35, T4)))
    elif gt < T5:
        put(render_scene(4, gt))
    elif gt < T5 + .45:                     # whip pan 180°
        p = ease(seg(gt, T5, T5 + .45))
        tmp, tc = new_surface()
        tc.set_source_surface(render_scene(4, gt), -p * W, 0); tc.paint()
        tc.set_source_surface(render_scene(5, gt), W * (1 - p), 0); tc.paint()
        put(motion_blur(tmp, math.sin(p * math.pi) * 220, axis=1))
    elif gt < T6:
        put(render_scene(5, gt))
    elif gt < T6 + .5:                      # tilt up to the guest
        p = ease(seg(gt, T6, T6 + .5))
        tmp, tc = new_surface()
        tc.set_source_surface(render_scene(5, gt), 0, p * H); tc.paint()
        tc.set_source_surface(render_scene(6, gt), 0, -H * (1 - p)); tc.paint()
        put(motion_blur(tmp, math.sin(p * math.pi) * 160, axis=0))
    elif gt < T7:
        put(render_scene(6, gt))
    else:                                   # blur, darken, build the logo
        e = ease(seg(gt, T7, T7 + .8))
        s6 = render_scene(6, gt)
        put(blur_surf(s6, 26 * e) if e > 0.02 else s6)
        setc(c, BG, .84 * e); c.paint()
        draw_end(c, gt)
    # fade in from black
    fi = 1 - ease(seg(gt, 0, .5))
    if fi > 0: setc(c, (0, 0, 0, fi)); c.paint()
    return out

if __name__ == '__main__':
    if sys.argv[1] == 'still':
        for ts in sys.argv[2:]:
            t0 = time.time(); f = frame(float(ts)); f.write_to_png(os.path.join(BUILD, f'still_{ts}.png'))
            print(ts, f'{time.time() - t0:.2f}s')
    elif sys.argv[1] == 'video':
        a, b = float(sys.argv[2]), float(sys.argv[3]); outp = sys.argv[4]
        n0, n1 = int(round(a * FPS)), int(round(b * FPS))
        p = subprocess.Popen([os.environ.get('FFMPEG', 'ffmpeg'), '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'bgra', '-s', f'{W}x{H}', '-r', str(FPS),
                              '-i', '-', '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p', outp], stdin=subprocess.PIPE)
        t0 = time.time()
        for i in range(n0, n1):
            f = frame(i / FPS); f.flush()
            p.stdin.write(bytes(f.get_data()))
            if (i - n0) % 30 == 0: print(i, f'{time.time() - t0:.1f}s', flush=True)
        p.stdin.close(); p.wait()
        print('done', time.time() - t0)
