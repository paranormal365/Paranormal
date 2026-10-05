"""Realistic-anime figures for the IsHaunted ad: real proportions (about seven heads tall), jointed
hips, knees, elbows and a leaning torso, so the poses can be dramatic. Cel shaded. Every character
carries a costume weapon. Heads reuse the anime face in common.py, drawn smaller.

figure(ctx, x, y, s, pose, outfit, t, view)   feet on (x, y) unless the pose is airborne
pose: dict of absolute joint angles in degrees (0 = straight down, +90 = toward +x):
      lt/lk, rt/rk   thigh / shin of the left / right leg (screen left / right in front view;
                     far / near in side view)
      la/lf, ra/rf   upper arm / forearm
      lean           torso tilt (+ = top toward +x), tilt = head tilt, air = True to skip grounding
"""
import math
import cairo
from common import *

HR = 58            # the head is drawn in the old 58-unit space ...
HK = 24 / HR       # ... at this scale
THIGH, SHIN, UPPER, FORE = 82, 78, 62, 58
HIP_Y = 160

# ── drawing primitives ─────────────────────────────────────────────────────
def _quad(ctx, x0, y0, x1, y1, a, b):
    dx, dy = x1 - x0, y1 - y0; L = math.hypot(dx, dy) or 1e-6
    nx, ny = -dy / L, dx / L
    ctx.move_to(x0 + nx * a, y0 + ny * a); ctx.line_to(x1 + nx * b, y1 + ny * b)
    ctx.line_to(x1 - nx * b, y1 - ny * b); ctx.line_to(x0 - nx * a, y0 - ny * a); ctx.close_path()
    return nx, ny

def limb(ctx, p0, p1, w0, w1, col, outline=3.0, shadow=.22):
    """a tapered limb with round joints, outlined, with a hard cel shadow down one side"""
    (x0, y0), (x1, y1) = p0, p1
    ctx.push_group()
    for grow, c in ((outline, OUTLINE), (0, col)):
        _quad(ctx, x0, y0, x1, y1, w0 / 2 + grow, w1 / 2 + grow); setc(ctx, c); ctx.fill()
        circle(ctx, x0, y0, w0 / 2 + grow); ctx.fill(); circle(ctx, x1, y1, w1 / 2 + grow); ctx.fill()
    nx, ny = _quad(ctx, x0, y0, x1, y1, 1, 1); ctx.new_path()
    ctx.set_operator(cairo.OPERATOR_ATOP)
    ctx.move_to(x0 + nx * w0 * .1, y0 + ny * w0 * .1); ctx.line_to(x1 + nx * w1 * .1, y1 + ny * w1 * .1)
    ctx.line_to(x1 + nx * w1, y1 + ny * w1); ctx.line_to(x0 + nx * w0, y0 + ny * w0); ctx.close_path()
    setc(ctx, (0, 0, 0, shadow)); ctx.fill()
    ctx.pop_group_to_source(); ctx.paint()

def _dir(a): r = math.radians(a); return math.sin(r), math.cos(r)

# ── the skeleton ───────────────────────────────────────────────────────────
def solve(pose, view):
    lean = pose.get('lean', 0.0)
    ux, uy = math.sin(math.radians(lean)), -math.cos(math.radians(lean))
    px, py = -uy, ux                       # perpendicular (toward +x when upright)
    H = (pose.get('hx', 0.0), -HIP_Y + pose.get('hy', 0.0))
    S = (H[0] + ux * 102, H[1] + uy * 102)
    N = (S[0] + ux * 16, S[1] + uy * 16)
    tilt = pose.get('tilt', 0.0)
    hd = (N[0] + ux * 26 + math.sin(math.radians(lean + tilt)) * 4, N[1] + uy * 26)
    side = view == 'side'
    sw, hw = (8, 5) if side else (34, 16)
    J = {'H': H, 'S': S, 'N': N, 'head': hd, 'lean': lean, 'tilt': tilt, 'u': (ux, uy), 'p': (px, py)}
    J['lsh'] = (S[0] - px * sw, S[1] - py * sw); J['rsh'] = (S[0] + px * sw, S[1] + py * sw)
    J['lhip'] = (H[0] - px * hw, H[1] - py * hw); J['rhip'] = (H[0] + px * hw, H[1] + py * hw)
    for side_, (ta, ka) in (('l', ('lt', 'lk')), ('r', ('rt', 'rk'))):
        hip = J[side_ + 'hip']
        d = _dir(pose.get(ta, 0)); knee = (hip[0] + d[0] * THIGH, hip[1] + d[1] * THIGH)
        d = _dir(pose.get(ka, 0)); ank = (knee[0] + d[0] * SHIN, knee[1] + d[1] * SHIN)
        J[side_ + 'knee'] = knee; J[side_ + 'ank'] = ank
    for side_, (ua, fa) in (('l', ('la', 'lf')), ('r', ('ra', 'rf'))):
        sh = J[side_ + 'sh']
        d = _dir(pose.get(ua, 0)); el = (sh[0] + d[0] * UPPER, sh[1] + d[1] * UPPER)
        d = _dir(pose.get(fa, 0)); wr = (el[0] + d[0] * FORE, el[1] + d[1] * FORE)
        J[side_ + 'el'] = el; J[side_ + 'wr'] = wr
    if not pose.get('air'):
        low = max(J['lank'][1], J['rank'][1], J.get('lknee', (0, -999))[1] + 8 if pose.get('kneel') == 'l' else -999,
                  J.get('rknee', (0, -999))[1] + 8 if pose.get('kneel') == 'r' else -999)
        dy = -low - 6
        for k, v in list(J.items()):
            if isinstance(v, tuple) and len(v) == 2 and k not in ('u', 'p'):
                J[k] = (v[0], v[1] + dy)
    return J

# ── poses ──────────────────────────────────────────────────────────────────
def P(**kw):
    base = dict(lt=4, lk=2, rt=-4, rk=-2, la=-10, lf=-6, ra=10, rf=6, lean=0, tilt=0)
    base.update(kw); return base

POSES = {
    'stand':   P(),
    'scared':  P(lt=10, lk=-8, rt=-10, rk=8, la=-30, lf=-160, ra=30, rf=160, lean=-4, tilt=-6),
    # a wide, low lunge, katana held up in two hands across the body
    'hero':    P(lt=-52, lk=-8, rt=58, rk=10, la=40, lf=150, ra=60, rf=150, lean=-6, hy=34, tilt=-4, weapon_ang=-38),
    # superhero landing: right knee down, left foot planted, fist to the ground, blade back
    'landing': P(lt=-70, lk=-4, rt=80, rk=-60, la=-10, lf=-4, ra=110, rf=96, lean=24, hy=70, kneel='r', weapon_ang=170),
    'leap':    P(lt=-60, lk=40, rt=60, rk=130, la=-150, lf=-170, ra=130, rf=170, lean=12, air=True, weapon_ang=-80),
    'guard':   P(lt=-22, lk=-4, rt=26, rk=6, la=-30, lf=60, ra=40, rf=-150, lean=6, hy=14, tilt=-6),
    'present': P(lt=-14, lk=-2, rt=30, rk=4, la=70, lf=88, ra=78, rf=92, lean=8, hy=10),
    'triumph': P(lt=-12, lk=-2, rt=12, rk=2, la=-20, lf=-10, ra=160, rf=178, tilt=-8),
    'villain': P(la=-150, lf=-176, ra=150, rf=176, lean=0, tilt=-10),
    'villain_lean': P(la=40, lf=80, ra=110, rf=60, lean=18, tilt=10),
    'flourish': P(lt=-10, lk=0, rt=26, rk=4, la=-110, lf=-130, ra=36, rf=150, lean=-6, tilt=-6, weapon_ang=-80),
    'attention': P(la=-6, lf=-2, ra=30, rf=160, weapon_ang=-90),
}

def walk(phase, run=False, lean=0.0):
    a = 34 if run else 24
    sw = math.sin(phase)
    k1 = max(0, -math.cos(phase)) * (60 if run else 30)
    k2 = max(0, math.cos(phase)) * (60 if run else 30)
    return P(lt=sw * a, lk=sw * a - k1, rt=-sw * a, rk=-sw * a - k2,
             la=-sw * a * .9, lf=-sw * a * .9 + (60 if run else 20), ra=sw * a * .9, rf=sw * a * .9 + (60 if run else 20),
             lean=lean + (10 if run else 2), hy=-abs(math.cos(phase)) * (8 if run else 4))

def conga(phase):
    """side view, hands forward on the shoulders ahead, 1-2-3-kick"""
    beat = (phase / (2 * math.pi)) % 1.0
    kick = math.sin(min(1, max(0, (beat - .75) / .25)) * math.pi)
    step = math.sin(phase * 3)
    return P(lt=step * 12, lk=step * 12 - 8, rt=-step * 12 + kick * 70, rk=-step * 12 + kick * 80,
             la=74, lf=88, ra=80, rf=94, lean=4 + kick * -6, hy=-abs(step) * 4)

def blend(p1, p2, k):
    out = {}
    for key in set(p1) | set(p2):
        a, b = p1.get(key, 0), p2.get(key, 0)
        out[key] = (lerp(a, b, k) if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool) else (b if k > .5 else a))
    return out

# ── weapons ────────────────────────────────────────────────────────────────
def weapon(ctx, kind, x, y, ang, t=0.0, s=1.0, glint=0.0):
    """ang: direction the business end points, degrees (0 = +x, 90 = down)"""
    ctx.save(); ctx.translate(x, y); ctx.rotate(math.radians(ang)); ctx.scale(s, s)
    def blade(L, w, col=hx('#DDE3EE'), tip=24, curve=0.0):
        ctx.move_to(0, -w / 2); ctx.curve_to(L * .5, -w / 2 - curve, L - tip, -w / 2 - curve, L, 0)
        ctx.line_to(L - tip, w / 2); ctx.line_to(0, w / 2); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(3); ctx.stroke_preserve()
        ctx.set_source(lingrad(0, -w, 0, w, [(0, hx('#FFFFFF')), (.5, col), (1, shade(col, .7))])); ctx.fill()
    if kind == 'katana':
        line(ctx, -34, 0, 6, 0, 9, hx('#2A2040')); ellipse(ctx, 8, 0, 4, 13); setc(ctx, hx('#C9A04A')); ctx.fill()
        ctx.translate(12, 0); blade(150, 8, tip=30, curve=6)
    elif kind == 'rapier':
        ctx.set_line_width(3); setc(ctx, hx('#C9A04A')); ctx.arc(4, 0, 12, -2.2, 2.2); ctx.stroke()
        line(ctx, -16, 0, 6, 0, 7, hx('#3A2010')); ctx.translate(6, 0); blade(160, 4, tip=12)
    elif kind == 'wand':
        line(ctx, -10, 0, 70, 0, 6, hx('#3A2414')); sparkle(ctx, 78, 0, 14 + 4 * math.sin(t * 8), hx('#B8FFE0'), .95, t * 2)
        glow(ctx, 78, 0, 40, LIME1, .4)
    elif kind == 'staff':
        line(ctx, -160, 0, 120, 0, 12, OUTLINE); line(ctx, -160, 0, 120, 0, 7, hx('#2B1A3A'))
        for k in range(3):
            ctx.set_line_width(4); setc(ctx, hx('#C9A04A')); ctx.arc(126, 0, 24 + k * 2, -1.0 - k * .5, 1.0 + k * .5); ctx.stroke()
        glow(ctx, 132, 0, 70, LIME1, .5 + .2 * math.sin(t * 5)); circle(ctx, 132, 0, 18); setc(ctx, hx('#E9FFB8')); ctx.fill()
        circle(ctx, 132, 0, 11); setc(ctx, LIME1); ctx.fill()
    elif kind == 'lance':
        line(ctx, -60, 0, 170, 0, 9, OUTLINE); line(ctx, -60, 0, 170, 0, 5, hx('#FFF4FB'))
        for i, c in enumerate(RAINBOW):
            line(ctx, 10 + i * 22, -1, 28 + i * 22, 1, 5, c)
        ctx.translate(170, 0); ctx.move_to(0, -10); ctx.line_to(48, 0); ctx.line_to(0, 10); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(3); ctx.stroke_preserve()
        ctx.set_source(lingrad(0, -10, 0, 10, [(0, hx('#FFE9A8')), (1, hx('#C9952A'))])); ctx.fill()
        sparkle(ctx, 50, 0, 12, hx('#FFFFFF'), .8 + .2 * math.sin(t * 6))
    elif kind == 'sword':
        line(ctx, -26, 0, 4, 0, 9, hx('#3A2010')); line(ctx, 6, -22, 6, 22, 7, hx('#C9A04A')); ctx.translate(8, 0); blade(130, 14, tip=18)
    elif kind == 'cutlass':
        line(ctx, -22, 0, 4, 0, 8, hx('#3A2010')); ctx.set_line_width(4); setc(ctx, hx('#C9A04A')); ctx.arc(2, 6, 14, 0, math.pi); ctx.stroke()
        ctx.translate(6, 0); blade(110, 16, tip=26, curve=10)
    elif kind == 'axe':
        line(ctx, -40, 0, 110, 0, 9, OUTLINE); line(ctx, -40, 0, 110, 0, 5, hx('#6A4424'))
        ctx.translate(96, 0); ctx.move_to(-6, -6); ctx.curve_to(10, -40, 34, -44, 40, -30); ctx.curve_to(30, -10, 30, 10, 40, 30)
        ctx.curve_to(34, 44, 10, 40, -6, 6); ctx.close_path(); setc(ctx, OUTLINE); ctx.set_line_width(3); ctx.stroke_preserve()
        ctx.set_source(lingrad(0, -40, 0, 40, [(0, hx('#FFFFFF')), (1, hx('#8E96A8'))])); ctx.fill()
    elif kind == 'scythe':
        line(ctx, -150, 0, 110, 0, 9, OUTLINE); line(ctx, -150, 0, 110, 0, 5, hx('#3A2A20'))
        ctx.translate(110, 0); ctx.move_to(0, -4); ctx.curve_to(-30, -60, -110, -80, -150, -60); ctx.curve_to(-100, -56, -40, -40, 0, 6); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(3); ctx.stroke_preserve()
        ctx.set_source(lingrad(0, -80, 0, 0, [(0, hx('#FFFFFF')), (1, hx('#8E96A8'))])); ctx.fill()
    elif kind == 'naginata':
        line(ctx, -120, 0, 110, 0, 9, OUTLINE); line(ctx, -120, 0, 110, 0, 5, hx('#5A1A1A'))
        ctx.translate(110, 0); blade(60, 10, tip=18, curve=8)
    elif kind == 'khopesh':
        line(ctx, -22, 0, 30, 0, 8, hx('#5A3A14')); ctx.translate(30, 0)
        ctx.move_to(0, -5); ctx.curve_to(40, -8, 60, -30, 80, -12); ctx.curve_to(70, -2, 40, 8, 0, 5); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(3); ctx.stroke_preserve(); setc(ctx, hx('#D9B25A')); ctx.fill()
    elif kind == 'daggers':
        ctx.translate(4, 0); blade(54, 8, tip=12)
    elif kind == 'toysword':
        line(ctx, -10, 0, 4, 0, 6, hx('#8A5A2A')); line(ctx, 5, -10, 5, 10, 5, hx('#8A5A2A'))
        rrect(ctx, 6, -4, 54, 8, 3); setc(ctx, hx('#C89A62')); ctx.fill()
    if glint > 0:
        sparkle(ctx, 120, -2, 26 * glint, (1, 1, 1, 1), glint, t * 3)
    ctx.restore()

def shield(ctx, x, y, s=1.0):
    ctx.save(); ctx.translate(x, y); ctx.scale(s, s)
    ctx.move_to(-30, -36); ctx.line_to(30, -36); ctx.curve_to(30, 10, 14, 30, 0, 42); ctx.curve_to(-14, 30, -30, 10, -30, -36); ctx.close_path()
    setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve()
    ctx.set_source(lingrad(-30, 0, 30, 0, [(0, hx('#2E5FB8')), (1, hx('#1E3E80'))])); ctx.fill()
    ctx.move_to(0, -30); ctx.line_to(0, 34); ctx.move_to(-24, -6); ctx.line_to(24, -6); ctx.set_line_width(6); setc(ctx, hx('#E2B23C')); ctx.stroke()
    ctx.restore()

# ── outfits ────────────────────────────────────────────────────────────────
def O(**kw):
    base = dict(skin=hx('#F2C9A8'), hair=hx('#3B2A20'), hair_style='short', top=hx('#555A6A'), pants=hx('#2C3550'),
                boots=hx('#1A1A22'), sleeve=None, hand=None, iris=hx('#5B3A29'), costume=None, weapon=None, weapon_hand='r',
                weapon_ang=None, second=None)
    base.update(kw); return base

OUTFITS = {
    'ninja':   O(hair=hx('#1C2340'), hair_style='spiky', top=hx('#232A4A'), pants=hx('#232A4A'), boots=hx('#14182A'),
                 hand=hx('#2A2F48'), iris=hx('#7C5CFF'), costume='ninja', weapon='katana'),
    'unicorn': O(skin=hx('#F7D6C4'), hair=hx('#FF8FD1'), hair_style='none', top=hx('#FFF4FB'), pants=hx('#FFF4FB'), boots=hx('#B7A3E0'),
                 hand=hx('#EADCF5'), iris=hx('#E05FB0'), costume='unicorn', weapon='lance'),
    'sorcerer': O(skin=hx('#B9A9D8'), hair=hx('#120A1E'), hair_style='long_dark', top=hx('#2A1340'), pants=hx('#2A1340'),
                  boots=hx('#120A1E'), sleeve=hx('#3A1A58'), hand=hx('#B9A9D8'), iris=LIME1, costume='sorcerer', weapon='staff', weapon_hand='l'),
    'vampire': O(skin=hx('#E8E4F0'), hair=hx('#14101C'), hair_style='slick', top=hx('#1A1020'), pants=hx('#1A1020'), boots=hx('#0A0A10'),
                 iris=hx('#C21E3A'), costume='vampire', weapon='rapier'),
    'witch':   O(skin=hx('#D9A07E'), hair=hx('#E0662A'), hair_style='long', top=hx('#5B2A86'), pants=hx('#2A1638'), boots=hx('#140A1C'),
                 iris=hx('#3E8A4A'), costume='witch', weapon='wand'),
    'knight':  O(top=hx('#9AA2B4'), pants=hx('#7A8296'), boots=hx('#5A6274'), hand=hx('#8A92A6'), costume='knight', weapon='sword', second='shield', hair_style='none'),
    'pirate':  O(skin=hx('#C68E6B'), hair=hx('#2A1A10'), top=hx('#9E1B32'), pants=hx('#2A2A3A'), boots=hx('#1A1210'), costume='pirate', weapon='cutlass', iris=hx('#2E6A9E')),
    'valkyrie': O(skin=hx('#F4D2BC'), hair=hx('#F2D27A'), hair_style='long', top=hx('#7A8296'), pants=hx('#4A3A2A'), boots=hx('#3A2A1A'),
                  costume='valkyrie', weapon='axe', iris=hx('#3A8AC8')),
    'reaper':  O(top=hx('#141418'), pants=hx('#141418'), boots=hx('#0A0A0C'), hand=hx('#E9E6DA'), costume='reaper', weapon='scythe', hair_style='none'),
    'samurai': O(skin=hx('#E9BE96'), hair=hx('#141418'), hair_style='none', top=hx('#8E1E28'), pants=hx('#2A1A20'), boots=hx('#141014'),
                 costume='samurai', weapon='naginata', iris=hx('#3A2414')),
    'mummy':   O(skin=hx('#D8CFB8'), top=hx('#D8CFB8'), pants=hx('#D8CFB8'), boots=hx('#B8AE96'), hand=hx('#D8CFB8'), hair_style='none', costume='mummy', weapon='khopesh'),
}

# ── heads ──────────────────────────────────────────────────────────────────
def head_shape(ctx, view):
    if view == 'side':
        ctx.move_to(-56, -4); ctx.curve_to(-60, -78, 54, -80, 58, -8)
        ctx.curve_to(60, 8, 66, 16, 62, 24); ctx.curve_to(58, 30, 54, 46, 40, 62); ctx.curve_to(26, 70, 0, 66, -14, 50)
        ctx.curve_to(-40, 40, -54, 24, -56, -4); ctx.close_path()
    else:
        ctx.move_to(-56, -4); ctx.curve_to(-60, -80, 60, -80, 56, -4)
        ctx.curve_to(54, 30, 24, 66, 0, 72); ctx.curve_to(-24, 66, -54, 30, -56, -4); ctx.close_path()

def draw_head(ctx, Ou, view, expr, t, look=(0, 0), blink=0.0, wind=1.0):
    cost = Ou['costume']; skin = Ou['skin']
    if cost == 'unicorn':
        unicorn_hood(ctx, 0, 0, HR, view, t, layer='back')
    if Ou['hair_style'] in ('long', 'long_dark'):
        ctx.move_to(-62, -10); ctx.curve_to(-80, 60, -70, 130, -40, 160); ctx.line_to(40, 160)
        ctx.curve_to(70, 130, 80, 60, 62, -10); ctx.close_path(); setc(ctx, OUTLINE); ctx.set_line_width(5); ctx.stroke_preserve(); setc(ctx, Ou['hair']); ctx.fill()
    if cost == 'reaper':
        # a deep hood and a skull
        ctx.move_to(-78, 40); ctx.curve_to(-90, -100, 90, -100, 78, 40); ctx.curve_to(60, 90, -60, 90, -78, 40); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(5); ctx.stroke_preserve(); setc(ctx, hx('#141418')); ctx.fill()
        head_shape(ctx, view); setc(ctx, hx('#E9E6DA')); ctx.fill()
        if view != 'back':
            ox = 14 if view == 'side' else 0
            for ex_ in ((-20, 20) if view == 'front' else (16, 40)):
                ellipse(ctx, ex_ + ox * 0, -2, 14, 16); setc(ctx, hx('#0A0A0C')); ctx.fill()
                circle(ctx, ex_, -2, 4); setc(ctx, hx('#FF4A3A')); ctx.fill(); glow(ctx, ex_, -2, 16, hx('#FF4A3A'), .6)
            ctx.move_to(ox, 16); ctx.line_to(ox - 6, 28); ctx.line_to(ox + 6, 28); ctx.close_path(); setc(ctx, hx('#0A0A0C')); ctx.fill()
            for i in range(6):
                line(ctx, ox - 20 + i * 8, 40, ox - 20 + i * 8, 50, 2.5, OUTLINE)
        ctx.move_to(-78, 40); ctx.curve_to(-90, -100, 90, -100, 78, 40)
        ctx.curve_to(56, -50, -56, -50, -78, 40); ctx.close_path(); setc(ctx, hx('#141418')); ctx.fill()
        return
    # the head
    ctx.set_line_width(5); setc(ctx, OUTLINE); head_shape(ctx, view); ctx.stroke_preserve(); setc(ctx, skin); ctx.fill()
    ctx.push_group()
    head_shape(ctx, view); setc(ctx, skin); ctx.fill()
    ctx.set_operator(cairo.OPERATOR_ATOP)
    ctx.rectangle(-90, -100, 180, 200); circle(ctx, -16, -16, 66); ctx.set_fill_rule(cairo.FILL_RULE_EVEN_ODD)
    setc(ctx, shade(skin, .84)); ctx.fill(); ctx.set_fill_rule(cairo.FILL_RULE_WINDING)
    ctx.pop_group_to_source(); ctx.paint()
    if view == 'front':
        for ex_ in (-56, 56):
            ellipse(ctx, ex_, 8, 9, 14); setc(ctx, shade(skin, .88)); ctx.fill()
    elif view == 'side':
        ellipse(ctx, -12, 8, 9, 14); setc(ctx, shade(skin, .88)); ctx.fill()
    if cost == 'mummy':
        ctx.save(); head_shape(ctx, view); ctx.clip()
        for i in range(9):
            line(ctx, -70, -60 + i * 16 + (i % 2) * 6, 70, -52 + i * 16 - (i % 2) * 6, 4, hx('#B8AE96'))
        ctx.restore()
    hs = Ou['hair_style']
    if hs == 'spiky':
        spiky_hair(ctx, 0, 0, HR, view, Ou['hair'], t, wind)
    elif hs == 'short':
        ctx.save(); ctx.move_to(-60, -4); ctx.curve_to(-66, -86, 66, -86, 60, -10)
        for i in range(6):
            ctx.line_to(56 - i * 22, -28 - (i % 2) * 12)
        ctx.close_path(); setc(ctx, Ou['hair']); ctx.fill(); ctx.restore()
    elif hs == 'slick':
        ctx.move_to(-60, -6); ctx.curve_to(-64, -84, 64, -84, 60, -6); ctx.line_to(14, -32); ctx.line_to(0, -10); ctx.line_to(-14, -32); ctx.close_path()
        setc(ctx, Ou['hair']); ctx.fill()
    elif hs in ('long', 'long_dark') and view != 'back':
        ctx.move_to(-62, 30); ctx.curve_to(-70, -86, 70, -86, 62, 30); ctx.line_to(50, 30); ctx.curve_to(50, -20, 20, -40, 0, -36)
        ctx.curve_to(-20, -40, -50, -20, -50, 30); ctx.close_path(); setc(ctx, Ou['hair']); ctx.fill()
    elif hs in ('long', 'long_dark'):
        circle(ctx, 0, -6, 62); setc(ctx, Ou['hair']); ctx.fill()
    if view != 'back' and cost not in ('knight',):
        face(ctx, 0, 0, view, expr, t, look=look, blink=blink, skin=skin, iris=Ou['iris'], eye_scale=.8)
    # hats, helmets, horns
    if cost == 'ninja':
        ninja_headband(ctx, 0, 0, HR, view, t, wind)
    elif cost == 'unicorn':
        unicorn_hood(ctx, 0, 0, HR, view, t, layer='front')
    elif cost == 'witch':
        ctx.save(); ctx.translate(0, -48); ctx.rotate(math.radians(-8))
        ellipse(ctx, 0, 0, 92, 18); setc(ctx, OUTLINE); ctx.fill(); ellipse(ctx, 0, -2, 88, 14); setc(ctx, hx('#2A1638')); ctx.fill()
        ctx.move_to(-46, -4); ctx.curve_to(-20, -70, 14, -120, 40, -140); ctx.curve_to(30, -96, 38, -44, 46, -4); ctx.close_path()
        setc(ctx, hx('#2A1638')); ctx.fill_preserve(); ctx.set_line_width(4); setc(ctx, OUTLINE); ctx.stroke()
        ctx.rectangle(-44, -24, 90, 14); setc(ctx, hx('#E07B1F')); ctx.fill(); ctx.restore()
    elif cost == 'sorcerer':
        for sg in (-1, 1):
            ctx.move_to(sg * 30, -54); ctx.curve_to(sg * 50, -110, sg * 90, -120, sg * 110, -150); ctx.curve_to(sg * 84, -110, sg * 64, -84, sg * 46, -40); ctx.close_path()
            setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve()
            ctx.set_source(lingrad(0, -150, 0, -40, [(0, hx('#5A4A6A')), (1, hx('#1A1020'))])); ctx.fill()
        rrect(ctx, -40, -62, 80, 14, 5); setc(ctx, hx('#C9A04A')); ctx.fill()
        circle(ctx, 0, -55, 8); setc(ctx, LIME1); ctx.fill(); glow(ctx, 0, -55, 24, LIME1, .6)
        if view != 'back':
            ctx.move_to(-10, 56); ctx.line_to(0, 96); ctx.line_to(10, 56); ctx.close_path(); setc(ctx, Ou['hair']); ctx.fill()
    elif cost == 'knight':
        head_shape(ctx, view); setc(ctx, OUTLINE); ctx.set_line_width(6); ctx.stroke_preserve()
        ctx.set_source(lingrad(-60, 0, 60, 0, [(0, hx('#DDE3EE')), (1, hx('#7A8296'))])); ctx.fill()
        if view != 'back':
            rrect(ctx, -44, -16, 88, 16, 4); setc(ctx, hx('#141820')); ctx.fill()
            for i in range(5):
                line(ctx, -30 + i * 15, 10, -30 + i * 15, 40, 3, hx('#4A5264'))
            glow(ctx, -18, -8, 14, ACC2, .6); glow(ctx, 18, -8, 14, ACC2, .6)
        ctx.move_to(-6, -70); ctx.curve_to(-30, -130, 30, -140, 60, -110); ctx.curve_to(20, -110, 10, -80, 6, -70); ctx.close_path()
        setc(ctx, hx('#C8233C')); ctx.fill()
    elif cost == 'pirate':
        ctx.save(); ctx.translate(0, -50)
        ctx.move_to(-96, 4); ctx.curve_to(-60, -20, -40, -70, 0, -70); ctx.curve_to(40, -70, 60, -20, 96, 4); ctx.curve_to(40, -10, -40, -10, -96, 4)
        ctx.close_path(); setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve(); setc(ctx, hx('#1A1420')); ctx.fill()
        line(ctx, -70, -4, 70, -4, 4, hx('#C9A04A')); ctx.restore()
        if view != 'back':
            line(ctx, -56, -30, 56, 10, 3, OUTLINE); ellipse(ctx, 22, -2, 15, 12); setc(ctx, OUTLINE); ctx.fill()
    elif cost == 'valkyrie':
        ctx.move_to(-60, -10); ctx.curve_to(-64, -90, 64, -90, 60, -10); ctx.close_path(); setc(ctx, OUTLINE); ctx.set_line_width(5); ctx.stroke_preserve()
        ctx.set_source(lingrad(-60, 0, 60, 0, [(0, hx('#DDE3EE')), (1, hx('#7A8296'))])); ctx.fill()
        for sg in (-1, 1):
            ctx.move_to(sg * 50, -50); ctx.curve_to(sg * 90, -80, sg * 96, -120, sg * 80, -150); ctx.curve_to(sg * 70, -110, sg * 64, -84, sg * 40, -66); ctx.close_path()
            setc(ctx, hx('#F2EAD8')); ctx.fill_preserve(); ctx.set_line_width(3); setc(ctx, OUTLINE); ctx.stroke()
    elif cost == 'samurai':
        ctx.move_to(-66, -6); ctx.curve_to(-70, -92, 70, -92, 66, -6); ctx.line_to(80, 10); ctx.line_to(-80, 10); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(5); ctx.stroke_preserve(); setc(ctx, hx('#2A1A20')); ctx.fill()
        ctx.move_to(0, -70); ctx.curve_to(-30, -120, -70, -130, -90, -126); ctx.move_to(0, -70); ctx.curve_to(30, -120, 70, -130, 90, -126)
        ctx.set_line_width(8); setc(ctx, hx('#E2B23C')); ctx.stroke()
    elif cost == 'vampire':
        if view != 'back':
            for fx_ in (-7, 7):
                ox = 30 if view == 'side' else 0
                ctx.move_to(ox + fx_ - 3, 30); ctx.line_to(ox + fx_, 40); ctx.line_to(ox + fx_ + 3, 30); ctx.close_path(); setc(ctx, (1, 1, 1, 1)); ctx.fill()

# ── body layers per costume ────────────────────────────────────────────────
def torso_path(ctx, J, view, grow=0.0, flare=0.0):
    S, H = J['S'], J['H']; px, py = J['p']; ux, uy = J['u']
    if view == 'side':
        a, b, c = 17 + grow, 14 + grow, 16 + grow
    else:
        a, b, c = 36 + grow, 24 + grow, 26 + grow + flare
    W = (H[0] + ux * 34, H[1] + uy * 34)
    pts = [(S[0] - px * a, S[1] - py * a), (W[0] - px * b, W[1] - py * b), (H[0] - px * c, H[1] - py * c + 10),
           (H[0] + px * c, H[1] + py * c + 10), (W[0] + px * b, W[1] + py * b), (S[0] + px * a, S[1] + py * a)]
    ctx.move_to(*pts[0])
    ctx.curve_to(pts[0][0], pts[0][1] + 30, pts[1][0], pts[1][1] - 20, *pts[1]); ctx.line_to(*pts[2]); ctx.line_to(*pts[3]); ctx.line_to(*pts[4])
    ctx.curve_to(pts[4][0], pts[4][1] - 20, pts[5][0], pts[5][1] + 30, *pts[5])
    ctx.curve_to(S[0] + px * a * .5, S[1] - 10, S[0] - px * a * .5, S[1] - 10, *pts[0]); ctx.close_path()

def figure(ctx, x, y, s, pose, Ou, t=0.0, view='front', expr='neutral', look=(0, 0), blink=0.0, wind=1.0,
           alpha=1.0, mirror=False, glint=0.0, hide_weapon=False, held=None):
    """held: optional callable(ctx, J) drawn in the hands (e.g. the scroll), in front of the body"""
    if alpha <= 0: return None
    if alpha < 1: ctx.push_group()
    ctx.save(); ctx.translate(x, y); ctx.scale(-s if mirror else s, s)
    J = solve(pose, view)
    cost = Ou['costume']
    top = Ou['top']; sleeve = Ou['sleeve'] or top; hand = Ou['hand'] or Ou['skin']
    near, far = ('r', 'l')
    ang_w = pose.get('weapon_ang')
    whand = Ou['weapon_hand']

    def hand_at(sd, col=None):
        w = J[sd + 'wr']; circle(ctx, w[0], w[1], 10); setc(ctx, OUTLINE); ctx.fill(); circle(ctx, w[0], w[1], 7.5); setc(ctx, col or hand); ctx.fill()

    def draw_weapon_in(sd):
        if hide_weapon or not Ou['weapon']: return
        if Ou['weapon'] == 'katana' and ang_w is None: return   # sheathed on the back
        w = J[sd + 'wr']; el = J[sd + 'el']
        a = ang_w if (ang_w is not None) else math.degrees(math.atan2(w[1] - el[1], w[0] - el[0])) - (70 if Ou['weapon'] in ('staff', 'lance', 'scythe', 'naginata', 'axe') else 10)
        weapon(ctx, Ou['weapon'], w[0], w[1], a, t, glint=glint)

    def arm(sd, behind=False):
        sh, el, wr = J[sd + 'sh'], J[sd + 'el'], J[sd + 'wr']
        col = shade(sleeve, .78) if behind else sleeve
        flare = 1.0 if cost == 'sorcerer' else 0.0
        limb(ctx, sh, el, 17 + flare * 8, 14 + flare * 10, col)
        limb(ctx, el, wr, 14 + flare * 10, 11 + flare * 16, col)
        if cost == 'ninja':
            limb(ctx, (lerp(el[0], wr[0], .72), lerp(el[1], wr[1], .72)), wr, 12, 11, hx('#7E8498'), outline=2)
        if cost == 'sorcerer':
            line(ctx, lerp(el[0], wr[0], .85) - 10, lerp(el[1], wr[1], .85), lerp(el[0], wr[0], .85) + 10, lerp(el[1], wr[1], .85), 3, hx('#C9A04A'))
        if sd == whand: draw_weapon_in(sd)
        if Ou.get('second') == 'shield' and sd != whand:
            shield(ctx, wr[0], wr[1], .9)
        hand_at(sd, shade(hand, .85) if behind else None)

    def leg(sd, behind=False):
        hip, kn, an = J[sd + 'hip'], J[sd + 'knee'], J[sd + 'ank']
        col = shade(Ou['pants'], .78) if behind else Ou['pants']
        limb(ctx, hip, kn, 24, 18, col); limb(ctx, kn, an, 18, 13, col)
        if cost == 'ninja':
            limb(ctx, (lerp(kn[0], an[0], .62), lerp(kn[1], an[1], .62)), an, 17, 14, hx('#7E8498'), outline=2)
        if cost in ('knight', 'valkyrie', 'samurai'):
            limb(ctx, (lerp(kn[0], an[0], .1), lerp(kn[1], an[1], .1)), an, 19, 15, hx('#9AA2B4'), outline=2)
        fwd = 16 if view == 'side' else 0
        ellipse(ctx, an[0] + fwd * .6, an[1] + 2, 15 if view == 'side' else 11, 8); setc(ctx, OUTLINE); ctx.fill()
        ellipse(ctx, an[0] + fwd * .6, an[1] + 1, 12 if view == 'side' else 8.5, 6); setc(ctx, shade(Ou['boots'], .8) if behind else Ou['boots']); ctx.fill()

    # ── back layer ──
    S = J['S']
    if cost == 'ninja' and view != 'back':
        ctx.save(); ctx.translate(S[0] - 6, S[1] - 6); ctx.scale(.6, .6)
        ninja_scarf_tail(ctx, 0, 0, t, wind, side=(view == 'side')); ctx.restore()
        if ang_w is None:   # katana sheathed on the back, hilt over the right shoulder
            a0 = (S[0] - 26, S[1] + 70); a1 = (S[0] + (6 if view == 'side' else 38), S[1] - 46)
            line(ctx, a0[0], a0[1], a1[0], a1[1], 13, OUTLINE); line(ctx, a0[0], a0[1], a1[0], a1[1], 8, hx('#2A2040'))
            line(ctx, lerp(a0[0], a1[0], .7), lerp(a0[1], a1[1], .7), a1[0], a1[1], 9, hx('#3A2E5A'))
    if cost in ('vampire', 'sorcerer', 'reaper'):
        cap = {'vampire': hx('#9E1B32'), 'sorcerer': hx('#1A0A2A'), 'reaper': hx('#0E0E12')}[cost]
        H = J['H']; flap = math.sin(t * 3) * 10 * wind
        ctx.move_to(S[0] - 40, S[1] - 6); ctx.curve_to(S[0] - 90 - flap, H[1] + 40, S[0] - 80 - flap, H[1] + 120, S[0] - 70 - flap * 1.5, H[1] + 150)
        ctx.line_to(S[0] + 70 + flap, H[1] + 150); ctx.curve_to(S[0] + 80 + flap, H[1] + 120, S[0] + 90 + flap, H[1] + 40, S[0] + 40, S[1] - 6); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve(); setc(ctx, cap); ctx.fill()
    if cost == 'sorcerer':
        # the tall spiked collar behind the head
        for k in range(5):
            ang = math.radians(-60 + k * 30)
            bx, by = S[0] + math.sin(ang) * 30, S[1] - 6
            ctx.move_to(bx - 14, by); ctx.line_to(S[0] + math.sin(ang) * 110, S[1] - 90 - math.cos(ang) * 70); ctx.line_to(bx + 14, by); ctx.close_path()
            setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve(); setc(ctx, hx('#3A1A58')); ctx.fill()
            line(ctx, bx, by, S[0] + math.sin(ang) * 104, S[1] - 84 - math.cos(ang) * 66, 2.5, hx('#C9A04A'))
    if cost == 'unicorn' and view != 'front':
        H = J['H']
        for i, c in enumerate(RAINBOW):
            ctx.save(); ctx.translate(H[0] - (16 if view == 'side' else 0), H[1] + 6 + i * 3); ctx.rotate(math.radians(130 + i * 8 + math.sin(t * 5 + i) * 6))
            ellipse(ctx, 0, -30, 7, 32); setc(ctx, c); ctx.fill(); ctx.restore()

    # ── limbs behind / body / limbs in front ──
    if view == 'side':
        arm(far, behind=True); leg(far, behind=True)
    else:
        leg('l'); leg('r')
    if view == 'side':
        leg(near)
    # torso
    flare = 34 if cost in ('witch', 'sorcerer', 'reaper', 'mummy') and cost != 'mummy' else 0
    torso_path(ctx, J, view, grow=3, flare=flare); setc(ctx, OUTLINE); ctx.fill()
    torso_path(ctx, J, view, flare=flare); setc(ctx, top); ctx.fill()
    ctx.push_group(); torso_path(ctx, J, view, flare=flare); setc(ctx, top); ctx.fill()
    ctx.set_operator(cairo.OPERATOR_ATOP); px, py = J['p']
    ctx.rectangle(S[0] + px * 8 - 4, S[1] - 40, 120, 260); setc(ctx, (0, 0, 0, .18)); ctx.fill()
    ctx.pop_group_to_source(); ctx.paint()
    if cost in ('witch', 'sorcerer', 'reaper'):
        # robes / skirt to the floor
        H = J['H']; lo = max(J['lank'][1], J['rank'][1]) - (6 if cost == 'witch' else 0)
        wide = 50 if view == 'front' else 34
        ctx.move_to(H[0] - wide * .7, H[1] - 10); ctx.line_to(H[0] - wide - 10, lo); ctx.line_to(H[0] + wide + 10, lo); ctx.line_to(H[0] + wide * .7, H[1] - 10); ctx.close_path()
        setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve(); setc(ctx, top); ctx.fill()
        if cost == 'sorcerer':
            line(ctx, H[0], H[1] - 10, H[0], lo, 4, hx('#C9A04A'))
    torso_details(ctx, J, view, Ou, t)
    if view == 'side':
        pass
    # head
    hd = J['head']
    ctx.save(); ctx.translate(*hd); ctx.rotate(math.radians(J['lean'] * .3 + J['tilt'])); ctx.scale(HK, HK)
    draw_head(ctx, Ou, view, expr, t, look, blink, wind)
    ctx.restore()
    if cost == 'ninja' and view != 'back':
        N = J['N']
        ellipse(ctx, N[0], N[1] + 4, 22, 9); setc(ctx, OUTLINE); ctx.fill(); ellipse(ctx, N[0], N[1] + 3, 20, 7); setc(ctx, ACC); ctx.fill()
    if view == 'side':
        arm(near)
    else:
        arm('l'); arm('r')
    if held: held(ctx, J)
    ctx.restore()
    if alpha < 1:
        ctx.pop_group_to_source(); ctx.paint_with_alpha(alpha)
    return J

def torso_details(ctx, J, view, Ou, t):
    cost = Ou['costume']; S, H = J['S'], J['H']; px, py = J['p']
    if cost == 'ninja':
        if view != 'back':
            line(ctx, S[0] - px * 26, S[1] - py * 26, H[0] + px * 6, H[1] - 40, 5, hx('#3A4470'))
            line(ctx, S[0] + px * 26, S[1] + py * 26, H[0] - px * 4, H[1] - 46, 5, hx('#3A4470'))
        else:
            line(ctx, S[0] - 30, H[1] - 10, S[0] + 34, S[1] - 30, 12, OUTLINE); line(ctx, S[0] - 30, H[1] - 10, S[0] + 34, S[1] - 30, 7, hx('#2A2040'))
        rrect(ctx, H[0] - 28, H[1] - 6, 56, 14, 5); setc(ctx, ACC); ctx.fill()
    elif cost == 'unicorn':
        if view != 'back':
            for k in range(3):
                circle(ctx, H[0] + (8 if view == 'side' else -10), S[1] + 22 + k * 22, 3.5); setc(ctx, hx('#E2B23C')); ctx.fill()
                if view == 'front':
                    circle(ctx, H[0] + 10, S[1] + 22 + k * 22, 3.5); ctx.fill()
            if view == 'front':
                for sg in (-1, 1):
                    rrect(ctx, S[0] + sg * 30 - 11, S[1] - 6, 22, 7, 3); setc(ctx, hx('#E2B23C')); ctx.fill()
    elif cost == 'vampire' and view != 'back':
        ctx.move_to(S[0] - 10, S[1]); ctx.line_to(S[0], S[1] + 40); ctx.line_to(S[0] + 10, S[1]); ctx.close_path(); setc(ctx, (1, 1, 1, 1)); ctx.fill()
        for sg in (-1, 1):
            ctx.move_to(S[0] + sg * 30, S[1] + 4); ctx.line_to(S[0] + sg * 48, S[1] - 40); ctx.line_to(S[0] + sg * 10, S[1] - 2); ctx.close_path()
            setc(ctx, hx('#1A1020')); ctx.fill()
    elif cost == 'sorcerer' and view != 'back':
        ctx.move_to(S[0] - 26, S[1]); ctx.line_to(S[0], S[1] + 80); ctx.line_to(S[0] + 26, S[1]); ctx.set_line_width(5); setc(ctx, hx('#C9A04A')); ctx.stroke()
        glow(ctx, S[0], S[1] + 26, 26, LIME1, .6); circle(ctx, S[0], S[1] + 26, 8); setc(ctx, LIME1); ctx.fill()
    elif cost == 'knight':
        ctx.save(); torso_path(ctx, J, view); ctx.clip()
        ctx.set_source(lingrad(S[0] - 40, 0, S[0] + 40, 0, [(0, hx('#E6EAF2')), (1, hx('#7A8296'))])); ctx.paint()
        rrect(ctx, S[0] - 22, S[1] + 14, 44, 70, 6); setc(ctx, hx('#2E5FB8')); ctx.fill()
        line(ctx, S[0], S[1] + 20, S[0], S[1] + 78, 6, hx('#E2B23C')); line(ctx, S[0] - 16, S[1] + 40, S[0] + 16, S[1] + 40, 6, hx('#E2B23C'))
        ctx.restore()
    elif cost == 'pirate' and view != 'back':
        line(ctx, S[0] - 30, S[1] + 4, H[0] + 24, H[1] - 6, 9, hx('#2A1A10'))
        rrect(ctx, H[0] - 28, H[1] - 8, 56, 12, 3); setc(ctx, hx('#2A1A10')); ctx.fill(); rrect(ctx, H[0] - 7, H[1] - 9, 14, 14, 2); setc(ctx, hx('#E2B23C')); ctx.fill()
        for k in range(3):
            circle(ctx, S[0] + 10, S[1] + 20 + k * 20, 3.5); setc(ctx, hx('#E2B23C')); ctx.fill()
    elif cost == 'valkyrie':
        ctx.save(); torso_path(ctx, J, view); ctx.clip()
        for k in range(6):
            line(ctx, S[0] - 40, S[1] + 10 + k * 14, S[0] + 40, S[1] + 10 + k * 14, 2, hx('#5A6274'))
        ctx.restore()
        rrect(ctx, H[0] - 28, H[1] - 8, 56, 12, 3); setc(ctx, hx('#6A4424')); ctx.fill()
    elif cost == 'samurai':
        ctx.save(); torso_path(ctx, J, view); ctx.clip()
        for k in range(5):
            rrect(ctx, S[0] - 40, S[1] + 6 + k * 18, 80, 14, 3); setc(ctx, shade(Ou['top'], 1.15 if k % 2 else .9)); ctx.fill()
            line(ctx, S[0] - 40, S[1] + 13 + k * 18, S[0] + 40, S[1] + 13 + k * 18, 2, hx('#E2B23C'))
        ctx.restore()
    elif cost == 'mummy':
        ctx.save(); torso_path(ctx, J, view); ctx.clip()
        for i in range(10):
            line(ctx, S[0] - 40, S[1] + i * 12 + (i % 2) * 5, S[0] + 40, S[1] + 6 + i * 12 - (i % 2) * 5, 3, hx('#B8AE96'))
        ctx.restore()
    elif cost == 'reaper' and view != 'back':
        line(ctx, S[0], S[1] + 6, S[0], H[1], 3, hx('#2A2A30'))

# ── the papyrus scroll (the printed pass) ──────────────────────────────────
def scroll(ctx, cx, cy, w, h, t=0.0, unroll=1.0, ang=0.0, detail=True):
    """an unrolled papyrus pass: the IsHaunted logo, the event, the QR, in brown ink"""
    hh = h * max(.08, unroll)
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(ang)
    # paper
    ctx.move_to(-w / 2, -hh / 2)
    for i in range(9):
        ctx.line_to(-w / 2 + w * (i + .5) / 9, -hh / 2 + (2 if i % 2 else -2)); ctx.line_to(-w / 2 + w * (i + 1) / 9, -hh / 2)
    ctx.line_to(w / 2, hh / 2)
    for i in range(9):
        ctx.line_to(w / 2 - w * (i + .5) / 9, hh / 2 + (2 if i % 2 else -2)); ctx.line_to(w / 2 - w * (i + 1) / 9, hh / 2)
    ctx.close_path()
    path = ctx.copy_path()
    setc(ctx, OUTLINE); ctx.set_line_width(4); ctx.stroke_preserve()
    g = cairo.RadialGradient(0, 0, w * .1, 0, 0, w * .8)
    g.add_color_stop_rgba(0, *hx('#F2E3B3')); g.add_color_stop_rgba(.7, *hx('#E2C98A')); g.add_color_stop_rgba(1, *hx('#B8914E'))
    ctx.set_source(g); ctx.fill()
    if detail and unroll > .6:
        ctx.save(); ctx.append_path(path); ctx.clip()
        # papyrus fibres
        for i in range(int(h / 6)):
            yy = -h / 2 + i * 6 + math.sin(i * 1.7) * 1.5
            line(ctx, -w / 2, yy, w / 2, yy + math.sin(i) * 2, 1, hx('#A88A4A', .25))
        for i in range(int(w / 10)):
            xx = -w / 2 + i * 10 + math.sin(i * 2.3) * 2
            line(ctx, xx, -h / 2, xx + 2, h / 2, 1, hx('#A88A4A', .12))
        ink = hx('#3A2410')
        k = w / 300
        a = clamp((unroll - .6) / .4)
        ctx.push_group()
        draw_logo(ctx, -w / 2 + 52 * k, -h / 2 + 52 * k, 70 * k)
        font(ctx, 'Irish Grover', 30 * k); setc(ctx, ink); ctx.move_to(-w / 2 + 96 * k, -h / 2 + 62 * k); ctx.show_text('IsHaunted')
        tw = text_w(ctx, 'IsHaunted'); ctx.move_to(-w / 2 + 96 * k + tw, -h / 2 + 62 * k); setc(ctx, hx('#5B3FBF')); ctx.show_text('.com')
        line(ctx, -w / 2 + 20 * k, -h / 2 + 98 * k, w / 2 - 20 * k, -h / 2 + 98 * k, 2 * k, hx('#6A4A20', .7))
        text(ctx, 'Admission scroll', 0, -h / 2 + 130 * k, hx('#7A3A10'), 'c', size=15 * k, bold=True)
        text(ctx, 'Halloween Masquerade', 0, -h / 2 + 160 * k, ink, 'c', 'Irish Grover', 27 * k)
        text(ctx, 'Ravenwood Manor · Sat 31 Oct · 8 PM', 0, -h / 2 + 184 * k, hx('#5A3A18'), 'c', size=13 * k)
        qs = 170 * k
        rrect(ctx, -qs / 2 - 8 * k, -h / 2 + 200 * k, qs + 16 * k, qs + 16 * k, 6 * k); setc(ctx, hx('#F6EBC8')); ctx.fill()
        draw_qr(ctx, -qs / 2, -h / 2 + 208 * k, qs, fg=hx('#2A1808'))
        text(ctx, 'Admit one · Sam Porter', 0, -h / 2 + 405 * k, ink, 'c', size=16 * k, bold=True)
        ctx.pop_group_to_source(); ctx.paint_with_alpha(a)
        ctx.restore()
    # wooden rollers, top and bottom
    for sy in (-1, 1):
        yy = sy * hh / 2
        rrect(ctx, -w / 2 - 10, yy - 12, w + 20, 24, 12); setc(ctx, OUTLINE); ctx.fill()
        ctx.set_source(lingrad(0, yy - 11, 0, yy + 11, [(0, hx('#C49A5A')), (.5, hx('#8A5A2A')), (1, hx('#5A3416'))]))
        rrect(ctx, -w / 2 - 8, yy - 10, w + 16, 20, 10); ctx.fill()
        for ex in (-w / 2 - 16, w / 2 + 16):
            circle(ctx, ex, yy, 13); setc(ctx, OUTLINE); ctx.fill(); circle(ctx, ex, yy, 10); setc(ctx, hx('#C9A04A')); ctx.fill()
    ctx.restore()
