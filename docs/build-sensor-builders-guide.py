"""Builds docs/IsHaunted-Sensor-Builders-Guide.pdf — for an engineer designing sensors the Paranormal
Platform can read during an investigation (Ben, 10/14/2026).

Ben: "write me a PDF styled with a look like the website and having images, sketches and schematics
that might be needed to design and incorporate sensors from a builder's perspective. He may build ones
that attach to the phone and connect using some type of USB-C hub or connection, or he might build them
to take readings having their own power supply and connect using Bluetooth."

Every figure is drawn here as SVG, so the PDF needs nothing but this file, the site's fonts and one
stock photograph. Schematics are reference sketches, not validated designs, and say so.

    python3 docs/build-sensor-builders-guide.py      # writes the HTML, then prints both PDFs with Chrome

Two editions from the same source: the site's dark look, and its light look for printing on white paper
(Ben, 10/14/2026: "create a light version ... so if printed, the background is white").
"""
import os
import subprocess

import site_doc_style

DOCS = os.path.dirname(os.path.abspath(__file__))
EDITIONS = {
    "dark": ("ishaunted-sensor-builders-guide.html", "IsHaunted-Sensor-Builders-Guide.pdf"),
    "light": ("ishaunted-sensor-builders-guide-light.html", "IsHaunted-Sensor-Builders-Guide-Light.pdf"),
}
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

# The service this guide assigns. Generated once (10/14/2026); every characteristic below is this base
# with its third and fourth bytes replaced, the usual way one vendor service carries its own family.
SERVICE_UUID = "8bda9059-8022-458e-87f1-1ffc5f0e8208"


def char_uuid(short):
    return f"8bda{short}-8022-458e-87f1-1ffc5f0e8208"


# ── Drawing helpers ──────────────────────────────────────────────────────────
# Site colors, plus an amber for power, which the site uses for warnings. The figures read these at
# drawing time, so use_palette() switches every figure between the two editions.
PALETTES = {
    # signal-tokens.css, dark mode.
    "dark": dict(INK="#E9EDF4", BODY="#C6CEDA", MUTED="#98A3B4", FAINT="#6C7889", SURFACE="#151C28",
                 RAISED="#1B2433", VIOLET="#7C5CFF", CYAN="#22D3EE", LILAC="#A78BFA", AMBER="#F2B544",
                 RED="#F87171", GRID="rgba(255,255,255,.035)"),
    # signal-tokens.css, light mode; amber and red darkened to read on white.
    "light": dict(INK="#0E1726", BODY="#2B3646", MUTED="#5A6679", FAINT="#8A94A6", SURFACE="#F4F7FC",
                  RAISED="#FFFFFF", VIOLET="#5B3DF5", CYAN="#0891A6", LILAC="#7C3AED", AMBER="#B7791F",
                  RED="#DC2626", GRID="rgba(14,23,38,.05)"),
}
INK = BODY = MUTED = FAINT = SURFACE = RAISED = VIOLET = CYAN = LILAC = AMBER = RED = GRID = ""


def use_palette(edition):
    globals().update(PALETTES[edition])


use_palette("dark")
FONT = "font-family=\"Public Sans, sans-serif\""
MONO = "font-family=\"ui-monospace, Menlo, monospace\""


def svg(width, height, body, *, sketch=False, label=""):
    """A figure. `sketch` draws it with a slight hand-drawn wobble, for the physical sketches."""
    defs = f"""<defs>
  <marker id="arr" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0,0 L10,5 L0,10 z" fill="{BODY}"/></marker>
  <marker id="arrc" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0,0 L10,5 L0,10 z" fill="{CYAN}"/></marker>
  <marker id="arra" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
    <path d="M0,0 L10,5 L0,10 z" fill="{AMBER}"/></marker>
  <filter id="wobble"><feTurbulence type="fractalNoise" baseFrequency="0.02" numOctaves="2" seed="7"/>
    <feDisplacementMap in="SourceGraphic" scale="2.6"/></filter>
  <pattern id="grid" width="16" height="16" patternUnits="userSpaceOnUse">
    <path d="M16 0 L0 0 0 16" fill="none" stroke="{GRID}" stroke-width="1"/></pattern>
</defs>"""
    group = f'<g filter="url(#wobble)">{body}</g>' if sketch else body
    bg = f'<rect width="{width}" height="{height}" rx="14" fill="{SURFACE}"/>'
    if sketch:
        bg += f'<rect width="{width}" height="{height}" rx="14" fill="url(#grid)"/>'
    return (f'<svg viewBox="0 0 {width} {height}" width="100%" xmlns="http://www.w3.org/2000/svg" '
            f'role="img" aria-label="{label}">{defs}{bg}{group}</svg>')


def box(x, y, w, h, title, sub="", *, color=None, fill=None, size=12):
    color, fill = color or VIOLET, fill or RAISED
    out = (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="9" fill="{fill}" stroke="{color}" stroke-width="1.6"/>'
           f'<text x="{x + w / 2}" y="{y + h / 2 + (-3 if sub else 4)}" text-anchor="middle" {FONT} font-size="{size}" '
           f'font-weight="700" fill="{INK}">{title}</text>')
    if sub:
        out += (f'<text x="{x + w / 2}" y="{y + h / 2 + 13}" text-anchor="middle" {FONT} font-size="9.5" '
                f'fill="{MUTED}">{sub}</text>')
    return out


def line(points, *, color=None, width=1.6, arrow="", dash=""):
    color = color or BODY
    d = " ".join(("M" if i == 0 else "L") + f"{x},{y}" for i, (x, y) in enumerate(points))
    marker = {"": "", "end": "url(#arr)", "cyan": "url(#arrc)", "amber": "url(#arra)"}[arrow]
    attrs = f' marker-end="{marker}"' if marker else ""
    if dash:
        attrs += f' stroke-dasharray="{dash}"'
    return f'<path d="{d}" fill="none" stroke="{color}" stroke-width="{width}" stroke-linejoin="round"{attrs}/>'


def text(x, y, words, *, size=10, color=None, anchor="start", weight="400", mono=False):
    color = color or BODY
    face = MONO if mono else FONT
    return (f'<text x="{x}" y="{y}" text-anchor="{anchor}" {face} font-size="{size}" font-weight="{weight}" '
            f'fill="{color}">{words}</text>')


def dot(x, y, color=None):
    color = color or BODY
    return f'<circle cx="{x}" cy="{y}" r="2.6" fill="{color}"/>'


def resistor(x, y, *, vertical=True, label="", color=None):
    color = color or BODY
    """A zig-zag resistor 40 long, starting at (x, y)."""
    if vertical:
        pts = [(x, y), (x, y + 8)] + [(x + (6 if i % 2 == 0 else -6), y + 10 + i * 4) for i in range(6)] + [(x, y + 32), (x, y + 40)]
        lab = text(x + 10, y + 24, label, size=9, color=MUTED)
    else:
        pts = [(x, y), (x + 8, y)] + [(x + 10 + i * 4, y + (6 if i % 2 == 0 else -6)) for i in range(6)] + [(x + 32, y), (x + 40, y)]
        lab = text(x + 20, y - 10, label, size=9, color=MUTED, anchor="middle")
    return line(pts, color=color, width=1.4) + lab


def capacitor(x, y, label=""):
    """A capacitor to ground, its top at (x, y)."""
    return (line([(x, y), (x, y + 12)]) + line([(x - 9, y + 12), (x + 9, y + 12)], width=2)
            + line([(x - 9, y + 17), (x + 9, y + 17)], width=2) + line([(x, y + 17), (x, y + 27)]) + ground(x, y + 27)
            + text(x + 12, y + 18, label, size=9, color=MUTED))


def ground(x, y):
    return (line([(x - 8, y), (x + 8, y)]) + line([(x - 5, y + 4), (x + 5, y + 4)])
            + line([(x - 2, y + 8), (x + 2, y + 8)]))


def chip(x, y, w, h, name, left=(), right=(), *, color=None):
    color = color or VIOLET
    """An IC: a box with named pins. `left`/`right` are pin names top to bottom."""
    out = (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="6" fill="{RAISED}" stroke="{color}" stroke-width="1.6"/>'
           + text(x + w / 2, y - 7, name, size=10.5, color=INK, anchor="middle", weight="700"))
    for side, pins in (("l", left), ("r", right)):
        step = h / (len(pins) + 1) if pins else 0
        for i, pin in enumerate(pins):
            py = y + step * (i + 1)
            if side == "l":
                out += line([(x - 14, py), (x, py)]) + text(x + 6, py + 3.5, pin, size=8.5, color=MUTED, mono=True)
            else:
                out += line([(x + w, py), (x + w + 14, py)]) + text(x + w - 6, py + 3.5, pin, size=8.5, color=MUTED, anchor="end", mono=True)
    return out


def pin_y(y, h, count, index):
    return y + h / (count + 1) * (index + 1)


def figure(svg_markup, caption):
    return f'<figure>{svg_markup}<figcaption>{caption}</figcaption></figure>'


# ── Figures ──────────────────────────────────────────────────────────────────

def fig_two_ways():
    b = ""
    # Left: a sensor on its own battery, over Bluetooth.
    b += text(30, 34, "ON ITS OWN BATTERY", size=9, color=CYAN, weight="700")
    b += box(30, 50, 170, 70, "Sensor node", "battery · radio · sensors", color=CYAN)
    b += line([(200, 85), (300, 85)], color=CYAN, arrow="cyan", dash="5 5")
    b += text(250, 76, "Bluetooth LE", size=9.5, color=CYAN, anchor="middle")
    # Right lower: wired.
    b += text(30, 170, "ON THE PHONE", size=9, color=AMBER, weight="700")
    b += box(30, 186, 170, 70, "Sensor accessory", "USB audio · USB network", color=AMBER)
    b += line([(200, 221), (245, 221)], color=AMBER, width=2.2)
    b += box(245, 196, 70, 50, "Hub", "PD in", color=AMBER, size=11)
    b += line([(315, 221), (345, 221), (345, 150)], color=AMBER, width=2.2, arrow="amber")
    b += text(353, 205, "USB-C", size=9.5, color=AMBER)
    # Phone.
    b += f'<rect x="300" y="38" width="96" height="112" rx="16" fill="{RAISED}" stroke="{INK}" stroke-width="1.8"/>'
    b += text(348, 84, "Paranormal", size=10.5, color=INK, anchor="middle", weight="700")
    b += text(348, 98, "Platform", size=10.5, color=INK, anchor="middle", weight="700")
    b += text(348, 116, "on the phone", size=9, color=MUTED, anchor="middle")
    # Onward.
    b += line([(396, 94), (470, 94)], arrow="end")
    b += text(433, 86, "session", size=9.5, color=MUTED, anchor="middle")
    b += box(470, 58, 120, 72, "The session", "readings + recordings", color=VIOLET)
    b += line([(590, 94), (640, 94), (640, 186)], arrow="end")
    b += box(560, 186, 110, 70, "Played back", "web · phone", color=VIOLET)
    b += text(618, 150, "upload", size=9.5, color=MUTED, anchor="end")
    return svg(690, 280, b, label="Two ways a sensor reaches a session")


def fig_ble_block():
    b = ""
    b += box(20, 40, 92, 50, "USB-C", "charge only", color=AMBER, size=11)
    b += line([(112, 65), (150, 65)], color=AMBER, arrow="amber")
    b += box(150, 40, 110, 50, "Charger", "single cell", color=AMBER, size=11)
    b += line([(205, 90), (205, 120)], color=AMBER, arrow="amber")
    b += box(150, 120, 110, 50, "LiPo cell", "1S · protected", color=AMBER, size=11)
    b += line([(260, 65), (300, 65)], color=AMBER, arrow="amber")
    b += box(300, 40, 110, 50, "LDO 3.3 V", "low noise", color=AMBER, size=11)
    b += line([(355, 90), (355, 128)], color=AMBER, arrow="amber")
    b += box(300, 128, 110, 60, "Radio MCU", "BLE module, certified", color=VIOLET, size=11)
    b += line([(260, 145), (285, 145), (285, 158), (300, 158)], color=AMBER)
    b += text(262, 138, "fuel gauge", size=8.5, color=MUTED)
    # I2C bus.
    b += line([(410, 158), (450, 158)], color=CYAN, width=2)
    b += line([(450, 70), (450, 250)], color=CYAN, width=2)
    b += text(458, 64, "I²C / SPI", size=9.5, color=CYAN)
    for i, (name, sub) in enumerate([("Magnetometer", "on the probe"), ("Temp + humidity", "SHT45 class"),
                                     ("Pressure", "BMP390 class"), ("Your sensor", "anything on the bus")]):
        y = 78 + i * 46
        b += line([(450, y + 18), (480, y + 18)], color=CYAN)
        b += box(480, y, 190, 36, name, sub, color=CYAN, size=10.5)
    b += box(150, 210, 110, 44, "Button + LED", "pair · status", color=MUTED, size=10.5)
    b += line([(260, 232), (330, 232), (330, 188)], color=MUTED)
    b += text(20, 290, "Amber: power.  Cyan: sensors.  Violet: the radio.", size=9, color=FAINT)
    return svg(690, 300, b, label="Block diagram of a battery sensor node")


def fig_power_schematic():
    b = ""
    # USB-C receptacle.
    b += chip(30, 60, 70, 120, "USB-C", right=("VBUS", "CC1", "CC2", "GND"), color=AMBER)
    vy = pin_y(60, 120, 4, 0); c1 = pin_y(60, 120, 4, 1); c2 = pin_y(60, 120, 4, 2); gy = pin_y(60, 120, 4, 3)
    # CC pulldowns.
    b += line([(114, c1), (150, c1), (150, 196)]) + resistor(150, 196, label="5.1k")
    b += line([(114, c2), (196, c2), (196, 196)]) + resistor(196, 196, label="5.1k")
    b += ground(150, 236) + ground(196, 236) + line([(114, gy), (124, gy), (124, 236)]) + ground(124, 236)
    b += text(120, 262, "CC pull-downs: the port", size=8.5, color=MUTED)
    b += text(120, 273, "offers 5 V to a sink", size=8.5, color=MUTED)
    # Charger.
    b += line([(114, vy), (250, vy)], color=AMBER, width=2)
    b += text(180, vy - 6, "5 V", size=9, color=AMBER, anchor="middle")
    b += capacitor(232, vy, "4.7µ")
    b += chip(264, 70, 110, 100, "Charger (MCP73831)", left=("VDD", "STAT", "PROG"), right=("VBAT", "VSS"), color=AMBER)
    b += line([(250, vy), (250, pin_y(70, 100, 3, 0))], color=AMBER, width=2)
    prog = pin_y(70, 100, 3, 2)
    b += line([(250, prog), (250, 196)]) + resistor(250, 196, label="2k ≈ 500 mA") + ground(250, 236)
    b += text(246, pin_y(70, 100, 3, 1) + 4, "LED", size=8.5, color=MUTED, anchor="end")
    vss = pin_y(70, 100, 2, 1)
    b += line([(388, vss), (400, vss), (400, 196)]) + ground(400, 196)
    vbat = pin_y(70, 100, 2, 0)
    b += line([(388, vbat), (450, vbat)], color=AMBER, width=2)
    # Battery.
    b += line([(450, vbat), (450, 150)], color=AMBER, width=2)
    b += line([(436, 150), (464, 150)], width=2.6, color=AMBER) + line([(442, 158), (458, 158)], width=2.6, color=AMBER)
    b += line([(450, 158), (450, 176)]) + ground(450, 176)
    b += text(470, 158, "LiPo 3.7 V", size=9, color=AMBER)
    b += text(470, 170, "with protection", size=8.5, color=MUTED)
    # LDO.
    b += line([(450, vbat), (520, vbat)], color=AMBER, width=2)
    b += chip(520, 74, 92, 70, "LDO 3.3 V", left=("IN", "EN"), right=("OUT", "GND"), color=AMBER)
    b += line([(612 + 14, pin_y(74, 70, 2, 0)), (660, pin_y(74, 70, 2, 0))], color=AMBER, width=2)
    b += text(662, pin_y(74, 70, 2, 0) + 4, "3V3", size=9.5, color=AMBER)
    b += capacitor(648, pin_y(74, 70, 2, 0), "1µ")
    b += line([(506, pin_y(74, 70, 2, 1)), (496, pin_y(74, 70, 2, 1)), (496, vbat)])
    b += line([(626, pin_y(74, 70, 2, 1)), (632, pin_y(74, 70, 2, 1)), (632, 176)]) + ground(632, 176)
    b += dot(496, vbat, AMBER)
    b += text(30, 30, "REFERENCE SKETCH — values are typical starting points, not a validated design", size=9, color=RED, weight="700")
    return svg(690, 290, b, label="Charging and power schematic sketch")


def fig_i2c_schematic():
    b = ""
    b += chip(40, 70, 130, 130, "Radio MCU", right=("3V3", "SDA", "SCL", "INT", "GND"))
    p = [pin_y(70, 130, 5, i) for i in range(5)]
    # The 3V3 rail: up from the pin, then across the top.
    b += line([(184, p[0]), (230, p[0]), (230, 40), (660, 40)], color=AMBER, width=1.8)
    b += text(236, 34, "3V3", size=9, color=AMBER)
    # Pull-ups from the rail to each line.
    for x, py, name in ((270, p[1], "SDA"), (320, p[2], "SCL")):
        b += dot(x, 40, AMBER) + resistor(x, 40) + line([(x, 80), (x, py)]) + dot(x, py, CYAN)
        b += text(x - 10, 64, "4.7k", size=9, color=MUTED, anchor="end")
    b += line([(184, p[1]), (660, p[1])], color=CYAN) + line([(184, p[2]), (660, p[2])], color=CYAN)
    b += text(196, p[1] - 5, "SDA", size=9, color=CYAN) + text(196, p[2] - 5, "SCL", size=9, color=CYAN)
    b += line([(184, p[4]), (660, p[4])])
    b += text(236, p[4] - 5, "GND", size=9, color=MUTED)
    # Devices on the bus.
    for i, (name, note) in enumerate([("SHT45", "temp + RH"), ("BMP390", "pressure"), ("Probe", "to the magnetometer")]):
        x = 380 + i * 96
        top = p[4] + 26
        b += f'<rect x="{x}" y="{top}" width="88" height="54" rx="6" fill="{RAISED}" stroke="{CYAN}" stroke-width="1.4"/>'
        b += text(x + 44, top + 22, name, size=9.5, color=INK, anchor="middle", weight="700")
        b += text(x + 44, top + 37, note, size=8, color=MUTED, anchor="middle")
        for k, py in enumerate((p[1], p[2], p[4])):
            px = x + 16 + k * 20
            b += line([(px, py), (px, top)], width=1.2) + dot(px, py, CYAN if k < 2 else BODY)
        b += line([(x + 80, 40), (x + 80, top)], color=AMBER, width=1.2) + dot(x + 80, 40, AMBER)
    b += line([(184, p[3]), (206, p[3]), (206, 262), (620, 262), (620, p[4] + 80)], color=LILAC, dash="4 4")
    b += text(400, 276, "data-ready / threshold interrupt: wake the radio only when there is something to send", size=8.5, color=LILAC, anchor="middle")
    b += text(40, 236, "Every part: 100 nF from 3V3 to GND,", size=8.5, color=MUTED)
    b += text(40, 248, "at its own pins.", size=8.5, color=MUTED)
    return svg(690, 290, b, label="Sensor bus schematic sketch")


def fig_elf_front_end():
    b = ""
    # Coil.
    for i in range(6):
        b += f'<path d="M{60 + i * 14},120 a7,9 0 0 1 14,0" fill="none" stroke="{CYAN}" stroke-width="2"/>'
    b += line([(60, 120), (60, 150), (120, 150)], color=CYAN) + line([(144, 120), (144, 100), (180, 100)], color=CYAN)
    b += line([(120, 150), (180, 150)], color=CYAN)
    b += text(102, 86, "pick-up coil", size=9.5, color=CYAN, anchor="middle")
    b += text(102, 176, "ferrite rod or air core", size=8.5, color=MUTED, anchor="middle")
    # In-amp.
    b += f'<path d="M180,80 L180,170 L260,125 Z" fill="{RAISED}" stroke="{VIOLET}" stroke-width="1.6"/>'
    b += text(208, 129, "INA", size=10, color=INK, weight="700")
    b += text(220, 196, "instrumentation amp", size=8.5, color=MUTED, anchor="middle")
    b += text(220, 207, "gain 100–1000", size=8.5, color=MUTED, anchor="middle")
    b += line([(260, 125), (300, 125)])
    b += box(300, 95, 120, 60, "Band-pass", "≈ 20 Hz – 2 kHz", size=11)
    b += line([(420, 125), (460, 125)])
    b += box(460, 95, 100, 60, "ADC / codec", "≥ 8 kS/s", size=11)
    b += line([(560, 125), (600, 125)], arrow="end")
    b += text(604, 120, "BLE: RMS +", size=9, color=MUTED)
    b += text(604, 132, "peak frequency", size=9, color=MUTED)
    b += text(604, 148, "USB audio:", size=9, color=MUTED)
    b += text(604, 160, "the waveform", size=9, color=MUTED)
    b += text(30, 250, "Mains (50/60 Hz) and its harmonics dominate indoors. Report the level AND the strongest frequency,",
              size=9, color=FAINT)
    b += text(30, 264, "so a reviewer can tell house wiring from anything else.", size=9, color=FAINT)
    return svg(690, 280, b, label="ELF magnetic field front end")


def fig_usb_paths():
    b = ""
    b += f'<rect x="24" y="40" width="96" height="190" rx="18" fill="{RAISED}" stroke="{INK}" stroke-width="1.8"/>'
    b += text(72, 130, "iPhone", size=11, color=INK, anchor="middle", weight="700")
    b += text(72, 146, "USB-C", size=9, color=MUTED, anchor="middle")
    b += line([(120, 135), (180, 135)], color=AMBER, width=2.4)
    b += box(180, 95, 90, 80, "Hub", "PD pass-through", color=AMBER, size=11)
    b += line([(225, 60), (225, 95)], color=AMBER, width=2, arrow="amber")
    b += text(232, 56, "charger in", size=9, color=AMBER)
    rows = [("USB Audio Class 2", "a microphone to iOS: waveforms, ELF, wideband sound", "works today · no Apple approval", CYAN),
            ("USB network (CDC-NCM / ECM)", "a tiny network: readings over a local socket", "prove on hardware first", LILAC),
            ("MFi accessory (iAP2)", "Apple's accessory program and its auth chip", "slow · Apple-gated", RED)]
    for i, (name, what, verdict, color) in enumerate(rows):
        y = 50 + i * 66
        b += line([(270, 135), (300, 135), (300, y + 25), (330, y + 25)], color=color, arrow="end" if False else "")
        b += f'<rect x="330" y="{y}" width="340" height="52" rx="9" fill="{RAISED}" stroke="{color}" stroke-width="1.5"/>'
        b += text(344, y + 19, name, size=10.5, color=INK, weight="700")
        b += text(344, y + 34, what, size=9, color=MUTED)
        b += text(656, y + 19, verdict, size=8.5, color=color, anchor="end", weight="700")
    b += text(24, 262, "iOS gives an app no general access to USB devices: an accessory has to be one of the kinds the system", size=9, color=FAINT)
    b += text(24, 275, "already understands. On a Lightning iPhone the same audio route works through Apple's USB camera adapter.", size=9, color=FAINT)
    return svg(690, 290, b, label="Wired routes into an iPhone")


def fig_packet():
    b = ""
    fields = [("ver", 1, VIOLET), ("flags", 1, VIOLET), ("sequence", 2, CYAN), ("device time (ms)", 4, CYAN),
              ("ch", 1, LILAC), ("value (int32, scaled)", 4, LILAC), ("ch", 1, LILAC), ("value", 4, LILAC), ("…", 2, FAINT)]
    unit = 30
    x = 20
    for name, size, color in fields:
        w = size * unit
        b += f'<rect x="{x}" y="60" width="{w}" height="46" rx="5" fill="{RAISED}" stroke="{color}" stroke-width="1.4"/>'
        b += text(x + w / 2, 87, name, size=9 if w > 40 else 8.5, color=INK, anchor="middle", weight="600")
        b += text(x + w / 2, 122, f"{size} B" if name != "…" else "", size=8.5, color=MUTED, anchor="middle")
        x += w + 2
    b += text(20, 40, "One notification = one sample time, any number of channels. Little-endian.", size=10, color=BODY)
    b += line([(20, 140), (20 + 8 * unit + 6, 140)], color=CYAN)
    b += text(20 + 4 * unit, 156, "header: 8 bytes", size=9, color=CYAN, anchor="middle")
    b += line([(20 + 8 * unit + 10, 140), (x - 4, 140)], color=LILAC)
    b += text((20 + 8 * unit + 10 + x) / 2, 156, "5 bytes per channel", size=9, color=LILAC, anchor="middle")
    b += text(20, 190, "flags  bit 0 = heartbeat (not an event) · bit 1 = buffered (sent after a reconnect) · bit 2 = out of range on any channel",
              size=9, color=MUTED)
    b += text(20, 206, "value  = reading × 10^scale, where scale and unit come from the channel's descriptor (read once at connect)",
              size=9, color=MUTED)
    b += text(20, 222, "A 20-byte notification carries the header and two channels; a negotiated larger MTU carries more.", size=9, color=MUTED)
    return svg(690, 240, b, label="Reading packet layout")


def fig_time_sync():
    b = ""
    b += text(20, 40, "Device clock", size=10, color=CYAN, weight="700")
    b += line([(130, 36), (670, 36)], color=CYAN, arrow="cyan")
    b += text(20, 150, "Phone clock", size=10, color=VIOLET, weight="700")
    b += line([(130, 146), (670, 146)], color=VIOLET, arrow="end")
    sends = [170, 260, 350, 440, 530]
    lat = [22, 48, 18, 35, 20]
    for s, l in zip(sends, lat):
        b += dot(s, 36, CYAN) + line([(s, 36), (s + l, 146)], color=BODY, width=1.2, arrow="end")
        b += text(s, 26, f"t={s - 150}", size=8.5, color=MUTED, anchor="middle", mono=True)
    b += text(350 + 18, 170, "fastest arrival", size=9, color=AMBER, anchor="middle")
    b += line([(368, 158), (368, 150)], color=AMBER)
    b += text(20, 200, "offset  = min over a window of (phone arrival − device time)   — the least-delayed sample is the honest one",
              size=9.5, color=BODY, mono=True)
    b += text(20, 218, "drift   = the slope of that offset over the session            — a 20 ppm crystal drifts 72 ms an hour",
              size=9.5, color=BODY, mono=True)
    b += text(20, 236, "at      = device time + offset(at that moment)               — each reading keeps its own time, even when buffered",
              size=9.5, color=BODY, mono=True)
    return svg(690, 255, b, label="Placing a sensor's samples on the phone's timeline")


def fig_placement_sketch():
    b = ""
    # Phone with compass zone.
    b += f'<rect x="60" y="60" width="110" height="200" rx="20" fill="none" stroke="{INK}" stroke-width="2"/>'
    b += f'<circle cx="115" cy="110" r="70" fill="rgba(248,113,113,.07)" stroke="{RED}" stroke-width="1.4" stroke-dasharray="6 5"/>'
    b += text(115, 30, "keep magnets and switching supplies", size=9.5, color=RED, anchor="middle", weight="700")
    b += text(115, 44, "out of here — the phone's own compass", size=9.5, color=RED, anchor="middle")
    b += text(115, 285, "phone", size=10, color=MUTED, anchor="middle")
    # Clip + stalk.
    b += line([(170, 200), (200, 200)], width=3) + line([(200, 200), (470, 140)], width=4, color=BODY)
    b += text(300, 148, "non-magnetic stalk, 15–25 cm", size=9.5, color=MUTED, anchor="middle")
    b += text(300, 162, "(fiberglass, carbon, plastic — not steel)", size=8.5, color=FAINT, anchor="middle")
    b += f'<rect x="470" y="112" width="70" height="52" rx="10" fill="none" stroke="{CYAN}" stroke-width="2"/>'
    b += text(505, 142, "probe", size=10, color=CYAN, anchor="middle", weight="700")
    b += text(505, 182, "magnetometer and", size=9, color=CYAN, anchor="middle")
    b += text(505, 194, "anything field-sensitive", size=9, color=CYAN, anchor="middle")
    # Battery body far end.
    b += f'<rect x="230" y="232" width="150" height="54" rx="10" fill="none" stroke="{AMBER}" stroke-width="2"/>'
    b += text(305, 262, "battery + radio", size=10, color=AMBER, anchor="middle", weight="700")
    b += line([(305, 232), (360, 162)], color=AMBER, dash="4 4")
    b += text(410, 260, "radio and battery live at the handle,", size=9, color=MUTED)
    b += text(410, 273, "away from both compasses", size=9, color=MUTED)
    b += text(560, 60, "No MagSafe", size=11, color=RED, weight="700")
    b += text(560, 76, "for a magnetic sensor:", size=9, color=MUTED)
    b += text(560, 89, "the ring is a strong", size=9, color=MUTED)
    b += text(560, 102, "permanent magnet.", size=9, color=MUTED)
    return svg(690, 300, b, sketch=True, label="Placement sketch: phone, stalk, probe")


def fig_enclosure_sketch():
    b = ""
    layers = [(70, "Lid", "light pipe for the LED · vent with membrane", MUTED),
              (120, "PCB", "radio module at one end, sensors at the other", VIOLET),
              (170, "LiPo cell", "foam-held; never under the magnetometer", AMBER),
              (220, "Base", "brass or nylon screws · USB-C port with a cover", MUTED)]
    for i, (y, name, note, color) in enumerate(layers):
        x = 120 + i * 18
        b += f'<path d="M{x},{y} L{x + 260},{y} L{x + 300},{y + 26} L{x + 40},{y + 26} Z" fill="none" stroke="{color}" stroke-width="2"/>'
        b += text(x + 320, y + 17, name, size=10.5, color=INK, weight="700")
        b += text(x + 320, y + 31, note, size=9, color=MUTED)
    b += line([(250, 96), (250, 118)], dash="3 4", arrow="end") + line([(268, 146), (268, 168)], dash="3 4", arrow="end")
    b += line([(286, 196), (286, 218)], dash="3 4", arrow="end")
    b += text(40, 40, "Exploded view of a hand-held node", size=11, color=INK, weight="700")
    b += text(40, 280, "Enclosure: ABS or PC. Avoid metal shells (they shield the radio) and ferrous hardware (it bends the field you measure).",
              size=9, color=FAINT)
    return svg(690, 295, b, sketch=True, label="Enclosure exploded sketch")


def fig_data_journey():
    b = ""
    steps = [("Sample", "sensor → MCU", CYAN), ("Packet", "BLE notification", CYAN), ("Reading", "placed in time", VIOLET),
             ("Session", "data.json · .ben", VIOLET), ("Timeline", "built once, shared", LILAC), ("Player", "chart · map · gauges", LILAC)]
    for i, (name, sub, color) in enumerate(steps):
        x = 14 + i * 113
        b += box(x, 50, 100, 62, name, sub, color=color, size=11)
        if i < len(steps) - 1:
            b += line([(x + 100, 81), (x + 113, 81)], arrow="end")
    b += text(14, 150, "What the builder owns: the first two boxes, and the descriptor that names each channel.", size=9.5, color=CYAN)
    b += text(14, 166, "What the platform owns: everything after the phone receives a packet.", size=9.5, color=LILAC)
    return svg(690, 185, b, label="From a sample to the player")


def fig_case_sketch():
    b = ""
    # Back view of a phone in the case.
    b += f'<rect x="60" y="40" width="170" height="320" rx="30" fill="none" stroke="{INK}" stroke-width="2.4"/>'
    b += f'<rect x="72" y="52" width="146" height="296" rx="22" fill="none" stroke="{MUTED}" stroke-width="1.2" stroke-dasharray="4 4"/>'
    b += text(145, 30, "back of the case", size=10, color=MUTED, anchor="middle")
    # Phone's own magnets.
    b += f'<rect x="84" y="66" width="62" height="62" rx="14" fill="rgba(248,113,113,.10)" stroke="{RED}" stroke-width="1.4"/>'
    b += text(115, 101, "camera", size=8.5, color=RED, anchor="middle")
    b += f'<circle cx="145" cy="200" r="44" fill="none" stroke="{RED}" stroke-width="1.4" stroke-dasharray="5 4"/>'
    b += text(145, 204, "MagSafe ring", size=8.5, color=RED, anchor="middle")
    b += f'<rect x="96" y="318" width="98" height="18" rx="6" fill="rgba(248,113,113,.10)" stroke="{RED}" stroke-width="1.2"/>'
    b += text(145, 331, "speakers · haptics", size=8, color=RED, anchor="middle")
    # Chin with the hub.
    b += f'<rect x="60" y="360" width="170" height="44" rx="14" fill="none" stroke="{AMBER}" stroke-width="2"/>'
    b += text(145, 380, "chin: plug + hub + MCU", size=9.5, color=AMBER, anchor="middle", weight="700")
    b += text(145, 394, "USB-C in for charging", size=8.5, color=MUTED, anchor="middle")
    # Side ports.
    for y in (150, 220, 290):
        b += f'<rect x="230" y="{y}" width="14" height="30" rx="4" fill="none" stroke="{CYAN}" stroke-width="1.8"/>'
        b += f'<rect x="44" y="{y}" width="14" height="30" rx="4" fill="none" stroke="{CYAN}" stroke-width="1.8"/>'
    b += text(256, 236, "module ports", size=9.5, color=CYAN, weight="700")
    b += text(256, 250, "both sides", size=8.5, color=MUTED)
    # Case magnetometer at the top corner away from the camera.
    b += f'<circle cx="206" cy="66" r="10" fill="none" stroke="{VIOLET}" stroke-width="2.2"/>'
    b += line([(216, 60), (300, 44)], color=VIOLET)
    b += text(304, 40, "case magnetometer", size=10, color=VIOLET, weight="700")
    b += text(304, 54, "the corner farthest from the hub,", size=8.5, color=MUTED)
    b += text(304, 66, "the ports and the camera module", size=8.5, color=MUTED)
    # Plug-in module.
    b += f'<rect x="300" y="290" width="110" height="60" rx="10" fill="none" stroke="{CYAN}" stroke-width="2"/>'
    b += text(355, 316, "sensor module", size=10, color=CYAN, anchor="middle", weight="700")
    b += text(355, 331, "ID chip inside", size=8.5, color=MUTED, anchor="middle")
    b += line([(300, 320), (262, 305)], color=CYAN, dash="4 4", arrow="cyan")
    # Optional probe arm.
    b += line([(206, 40), (206, 14), (360, 14)], color=LILAC, dash="5 4")
    b += text(366, 18, "optional: a fold-out arm puts the magnetometer farther still", size=8.5, color=LILAC)
    # iPad note.
    b += f'<rect x="470" y="120" width="200" height="250" rx="22" fill="none" stroke="{INK}" stroke-width="2"/>'
    b += line([(470, 150), (470, 340)], color=RED, width=4)
    b += line([(670, 150), (670, 340)], color=RED, width=4)
    b += text(570, 112, "iPad case", size=10, color=MUTED, anchor="middle")
    b += text(570, 230, "edge magnets for", size=9, color=RED, anchor="middle")
    b += text(570, 243, "the Pencil and keyboard", size=9, color=RED, anchor="middle")
    b += f'<circle cx="570" cy="150" r="10" fill="none" stroke="{VIOLET}" stroke-width="2.2"/>'
    b += text(570, 178, "magnetometer: top center,", size=8.5, color=VIOLET, anchor="middle")
    b += text(570, 190, "clear of the edges", size=8.5, color=VIOLET, anchor="middle")
    b += f'<rect x="530" y="370" width="80" height="26" rx="8" fill="none" stroke="{AMBER}" stroke-width="1.8"/>'
    b += text(570, 387, "hub", size=9, color=AMBER, anchor="middle")
    return svg(690, 420, b, sketch=True, label="Sketch of a sensor case for iPhone and iPad")


def fig_case_block():
    b = ""
    b += box(20, 120, 90, 56, "Phone", "USB-C", color=INK, size=11)
    b += line([(110, 148), (150, 148)], color=AMBER, width=2.2)
    b += box(150, 110, 120, 76, "PD + hub", "pass-through charging", color=AMBER, size=11)
    b += box(150, 24, 120, 50, "USB-C in", "wall charger / battery", color=AMBER, size=10.5)
    b += line([(210, 74), (210, 110)], color=AMBER, width=2, arrow="amber")
    b += line([(270, 148), (310, 148)], color=AMBER, width=2)
    b += box(310, 104, 130, 88, "Case MCU", "gathers every module", color=VIOLET, size=11)
    b += text(375, 210, "to the phone, one of:", size=8.5, color=MUTED, anchor="middle")
    b += text(375, 222, "USB network + USB audio", size=8.5, color=LILAC, anchor="middle")
    b += text(375, 234, "or Bluetooth LE (USB for power)", size=8.5, color=CYAN, anchor="middle")
    b += line([(440, 148), (480, 148)], color=CYAN, width=2)
    b += box(480, 120, 90, 56, "I²C mux", "a bus per port", color=CYAN, size=10.5)
    for i, name in enumerate(["Port 1", "Port 2", "Port 3", "Magnetometer"]):
        y = 30 + i * 62
        b += line([(570, 148), (590, 148), (590, y + 20), (600, y + 20)], color=CYAN)
        b += box(596, y, 88, 40, name, "", color=CYAN if i < 3 else VIOLET, size=9.5)
    return svg(690, 270, b, label="Sensor case electronics")


def fig_print_section():
    b = ""
    b += text(30, 30, "Cross-section through the case's chin and back (not to scale)", size=10.5, color=INK, weight="700")
    # Phone.
    b += f'<rect x="120" y="70" width="450" height="46" rx="10" fill="none" stroke="{INK}" stroke-width="2"/>'
    b += text(345, 98, "phone", size=10, color=MUTED, anchor="middle")
    # TPU bumper wrapping the edge.
    b += f'<path d="M120,60 Q96,60 96,92 Q96,126 120,126" fill="none" stroke="{LILAC}" stroke-width="6"/>'
    b += f'<path d="M570,60 Q594,60 594,92 Q594,126 570,126" fill="none" stroke="{LILAC}" stroke-width="6"/>'
    b += text(608, 80, "TPU bumper", size=9.5, color=LILAC, weight="700")
    b += text(608, 93, "grips the phone", size=8.5, color=MUTED)
    # Rigid shell.
    b += f'<path d="M96,126 L96,196 Q96,214 114,214 L576,214 Q594,214 594,196 L594,126" fill="none" stroke="{AMBER}" stroke-width="3"/>'
    b += f'<path d="M108,126 L582,126" fill="none" stroke="{AMBER}" stroke-width="2"/>'
    b += text(608, 170, "PETG / ASA shell", size=9.5, color=AMBER, weight="700")
    b += text(608, 183, "2–2.4 mm walls", size=8.5, color=MUTED)
    # Electronics bay.
    b += f'<rect x="140" y="140" width="220" height="56" rx="6" fill="none" stroke="{VIOLET}" stroke-width="1.8"/>'
    b += text(250, 166, "electronics bay", size=9.5, color=VIOLET, anchor="middle", weight="700")
    b += text(250, 180, "board on brass inserts", size=8.5, color=MUTED, anchor="middle")
    for x in (156, 344):
        b += f'<rect x="{x - 5}" y="182" width="10" height="20" rx="2" fill="none" stroke="{AMBER}" stroke-width="1.6"/>'
    b += line([(160, 222), (196, 250)], color=AMBER) + text(200, 258, "heat-set brass insert", size=8.5, color=AMBER)
    # Magnetometer pocket at the far end.
    b += f'<rect x="510" y="150" width="40" height="36" rx="5" fill="none" stroke="{CYAN}" stroke-width="1.8"/>'
    b += text(530, 240, "magnetometer pocket", size=8.5, color=CYAN, anchor="middle", weight="700")
    b += text(530, 252, "printed clips, no screws", size=8.5, color=MUTED, anchor="middle")
    b += line([(530, 186), (530, 228)], color=CYAN, dash="3 3")
    # Cable channel.
    b += line([(360, 168), (420, 168), (440, 140), (500, 140)], color=BODY, dash="5 4")
    b += text(470, 158, "ribbon channel", size=8.5, color=MUTED, anchor="middle")
    # USB-C plug at the chin.
    b += f'<rect x="40" y="84" width="40" height="18" rx="4" fill="none" stroke="{AMBER}" stroke-width="2"/>'
    b += line([(80, 93), (118, 93)], color=AMBER, width=2)
    b += text(30, 140, "USB-C plug,", size=8.5, color=AMBER)
    b += text(30, 152, "strain-relieved", size=8.5, color=AMBER)
    b += text(30, 286, "Openings for the microphones, speakers, camera and flash stay fully open: a case that covers the phone's",
              size=9, color=FAINT)
    b += text(30, 299, "microphones changes every recording made through it.", size=9, color=FAINT)
    return svg(690, 312, b, sketch=True, label="Cross-section sketch of a 3D-printed case")


# ── The document ─────────────────────────────────────────────────────────────

GATT_ROWS = [
    ("Device Information", "0x180A", "Manufacturer 0x2A29 · Model 0x2A24 · Serial 0x2A25 · Firmware 0x2A26", "read",
     "The session's <code>device</code> block, word for word."),
    ("Battery", "0x180F", "Battery Level 0x2A19 (uint8, %)", "read · notify",
     "<code>battery_percent_at_start</code>, and the <code>battery</code> channel."),
    ("Environmental Sensing", "0x181A", "Temperature 0x2A6E · Humidity 0x2A6F · Pressure 0x2A6D · Magnetic Flux Density 3D 0x2AA1",
     "read · notify", "Use the standard ones where they fit: any BLE tool can read them."),
    ("IsHaunted Sensor", SERVICE_UUID, "the characteristics in the next table", "—",
     "Everything the standard services cannot say."),
]

CUSTOM_ROWS = [
    ("Readings", char_uuid("0001"), "notify", "The packet above, one per sample time."),
    ("Channels", char_uuid("0002"), "read", "JSON: each channel's id, name, unit, scale, accuracy and range."),
    ("Control", char_uuid("0003"), "write", "Start, stop, rate, set base level, report-at threshold."),
    ("Clock", char_uuid("0004"), "read", "The device's millisecond counter now (uint32), for the first offset."),
    ("Status", char_uuid("0005"), "read · notify", "Warm-up done, calibration date, faults, buffered count."),
]

SENSORS = [
    ("Magnetic field, 3-axis", "emf, emf_x/y/z", "uT", "LIS3MDL, MMC5983MA, RM3100", "I²C/SPI",
     "The core instrument. RM3100-class parts are far quieter than a phone's compass. Calibrate hard and soft iron per unit."),
    ("ELF (AC) magnetic", "elf_field + elf_frequency", "uT, Hz", "coil + INA333/INA128 + ADC or codec", "analog",
     "What hand-held EMF meters actually show. Report level and the strongest frequency."),
    ("Electric field (static / ELF)", "e_field", "relative", "antenna + electrometer op-amp (LMP7721)", "analog",
     "Very high impedance: guard the input trace. Report change from base, not an absolute."),
    ("Temperature + humidity", "temperature, humidity", "degC, pct", "SHT45, BME280", "I²C",
     "Ventilate the sensor away from the battery and regulator, which warm it."),
    ("Air pressure", "pressure", "hPa", "BMP390, LPS22", "I²C", "Doors, drafts and floors: small, fast changes are the interesting part."),
    ("Surface temperature (non-contact)", "ir_temperature", "degC", "MLX90614, MLX90632", "I²C", "Cold spots on walls and floors; note the field of view."),
    ("Thermal array", "thermal_min / thermal_max", "degC", "AMG8833 (8×8), MLX90640 (32×24)", "I²C",
     "Send min/max/mean as readings; a frame is a picture, not a reading (attach it as a file)."),
    ("Light", "illuminance", "lx", "VEML7700, TSL2591", "I²C", "Flashlights, flickers, a door opening onto a lit hall."),
    ("Vibration / footfalls", "vibration", "mm/s or g", "geophone SM-24 + ADS1220, or ADXL355", "SPI/analog",
     "A geophone hears footsteps in another room; mount it on the floor, not the table."),
    ("Radiation", "radiation", "uSv/h or cpm", "PIN-diode module (BG51), or a GM tube", "pulse",
     "A GM tube needs ~400 V: see the safety note. The PIN-diode route avoids high voltage."),
    ("Presence (radar)", "presence", "bool + m", "60 GHz mmWave modules", "UART",
     "Sees movement through thin walls. It is a transmitter: keep it off during EMF work."),
    ("Passive infrared", "motion_detected", "bool", "Panasonic EKMC series", "GPIO", "Cheap, reliable, and exactly what the format's suggested name expects."),
]


def table(headers, rows, cls=""):
    head = "".join(f"<th>{h}</th>" for h in headers)
    body = "".join("<tr>" + "".join(f"<td>{c}</td>" for c in row) + "</tr>" for row in rows)
    return f'<table class="{cls}"><thead><tr>{head}</tr></thead><tbody>{body}</tbody></table>'


EXTRA = """
.lead { font-size: 11.6pt; color: var(--ink); line-height: 1.55; }
.cols { display: grid; grid-template-columns: 1fr 1fr; gap: .18in; margin: .1in 0 .18in; }
.card { background: var(--surface); border: 1px solid var(--line); border-radius: 12px; padding: .14in .18in; break-inside: avoid; }
.card h4 { margin: 0 0 .06in; font-size: 11pt; }
.card p, .card ul { font-size: 9.4pt; margin-bottom: .04in; }
.card.cyan { border-top: 3px solid #22D3EE; } .card.amber { border-top: 3px solid #F2B544; } .card.violet { border-top: 3px solid #7C5CFF; }
.warn { background: rgba(248,113,113,.10); border-left: 3px solid #F87171; border-radius: 0 10px 10px 0; padding: .1in .16in; margin: 0 0 .14in; }
.warn b { color: #FCA5A5; }
.check li { list-style: none; margin-left: -.18in; }
.check li::before { content: "☐  "; color: var(--accent2); }
td code { white-space: nowrap; }
.uuid { font-size: 7.6pt; white-space: nowrap; }
.pagebreak { break-before: page; page-break-before: always; }
/* A long table may cross a page; its rows may not, and the header repeats. */
table.long { page-break-inside: auto; break-inside: auto; }
table.long tr { break-inside: avoid; page-break-inside: avoid; }
table.long thead { display: table-header-group; }
figure svg { display: block; }
"""


# The light edition: the site's light-mode values over the same rules, so a printed copy is ink on
# white paper. The cover keeps its photograph — it sits in a card with a margin — and loses the
# full-bleed dark page around it.
LIGHT = """
:root {
  --bg: #FFFFFF; --sunken: #F4F7FC; --surface: #F4F7FC; --raised: #EAEFF7;
  --ink: #0E1726; --body: #2B3646; --muted: #5A6679; --faint: #8A94A6;
  --line: rgba(14,23,38,.11); --line-strong: rgba(14,23,38,.20);
  --accent: #5B3DF5; --accent2: #0891A6; --link: #5B3DF5; --soft: rgba(91,61,245,.08);
  --grad: linear-gradient(120deg, #5B3DF5, #0891A6);
  --glow: 0 8px 22px rgba(91,61,245,.30);
}
@page { background: #FFFFFF; }
body { background: #FFFFFF; }
td { background: #FFFFFF; }
.accent-text { color: #A78BFA; }
.warn { background: rgba(220,38,38,.06); border-left-color: #DC2626; }
.warn b { color: #B91C1C; }
.card.cyan { border-top-color: #0891A6; } .card.amber { border-top-color: #B7791F; } .card.violet { border-top-color: #5B3DF5; }
.site-cover { background: #FFFFFF; }
.site-cover .kicker { color: #22D3EE; } .site-cover .brand span { color: #A78BFA; }
.site-cover .hero { box-shadow: none; }
.site-cover .fact { background: #F4F7FC; }
.site-cover .about { background: #F4F7FC; }
"""


def build(edition):
    use_palette(edition)
    html_out, pdf_out = (os.path.join(DOCS, name) for name in EDITIONS[edition])
    e = site_doc_style
    theme = LIGHT if edition == "light" else ""
    parts = ["<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">",
             "<title>Sensor Builder's Guide — IsHaunted</title>",
             f"<style>{e.css(DOCS)}{EXTRA}{theme}</style></head><body>"]
    parts.append(e.cover(
        DOCS,
        kicker="The Paranormal Platform · for hardware builders",
        title_html="Building sensors <span class=\"accent-text\">for an investigation</span>",
        lede="How to design an instrument the Paranormal Platform can read while people investigate: on its own battery "
             "over Bluetooth, or attached to the phone over USB-C.",
        photo="i1-flashlight-silhouette.jpg",
        facts=[("Wireless", "Bluetooth LE"), ("Wired", "USB-C audio · network"), ("Data", "Device Data Format v1")],
        about_html="<b>Who this is for:</b> an electrical engineer designing new sensors. It covers what the phone can and "
                   "cannot accept, a reference architecture for each route, the over-the-air contract, placement and "
                   "noise, and how a reading ends up on the investigators' timeline. <b>The schematics are reference "
                   "sketches</b> — starting points to design from, not validated circuits.",
        foot_right="Sensor Builder's Guide · 10/14/2026"))

    # 1. The short version.
    parts.append(f"""
<h2>The short version</h2>
<p class="lead">The Paranormal Platform turns a phone into an instrument panel: it reads the phone's own magnetometer,
microphone, camera and location many times a second, keeps each reading against one clock, and plays a whole night back
as one timeline. A sensor you build becomes one more channel on that timeline.</p>
{figure(fig_two_ways(), "Two ways in. Either way, the phone places each reading on the session's clock, and everything "
        "downstream — the upload, the web player, the phone's own review — already knows how to carry a new channel.")}
<div class="cols">
  <div class="card cyan"><h4>Start with Bluetooth LE</h4>
    <p>A sensor on its own battery, talking Bluetooth LE. No Apple approval, works with every iPhone the app supports,
    and the sensor's own electronics stay away from the phone's compass. <b>Recommended for a first instrument.</b></p></div>
  <div class="card amber"><h4>Wired, for waveforms</h4>
    <p>An accessory on the phone's USB-C port, through a hub that also charges the phone. Best when the signal is fast —
    an ELF waveform, wideband sound — and the accessory can present itself as a USB microphone.</p></div>
</div>
<blockquote><p><b>What exists today, and what does not.</b> The format your readings travel in, the upload, the
timeline and both players exist and are built to take new channels. The phone's side of a new sensor — finding it,
pairing, reading its channels, drawing its gauge — is built when the first instrument is ready to test against.
This guide is the contract both sides build to.</p></blockquote>
""")

    # 2. What the phone accepts.
    parts.append(f"""
<h2>What the phone will accept</h2>
<p>An iPhone is not a USB host an app can program. iOS gives apps no general access to USB devices; an accessory has to
be one of the kinds the system already understands. That narrows the wired choices to three, and makes Bluetooth LE
the path of least resistance.</p>
{figure(fig_usb_paths(), "Wired routes. A hub with power-delivery pass-through keeps the phone charging through a long "
        "night while the accessory is connected.")}
{table(["Route", "How the app reads it", "Apple approval", "Good for"], [
    ["<b>Bluetooth LE</b>", "Core Bluetooth, as a GATT client", "None", "Slow-changing channels at up to tens of samples a second; anything on its own battery"],
    ["<b>USB Audio Class 2</b>", "The audio input, like an external microphone", "None", "Waveforms: ELF field, infrasound, ultrasonic, a better microphone"],
    ["<b>USB network</b> (CDC-NCM/ECM)", "A local socket to the accessory's address", "None", "Many channels at a high rate. <b>Prove on hardware first</b>: iPhones accept USB Ethernet adapters, but test a custom device early"],
    ["<b>MFi accessory</b>", "External Accessory framework (iAP2)", "Apple's MFi program + authentication chip", "A volume product, later. Not for a first build"],
])}
<p><b>Phones with Lightning</b> (iPhone 14 and earlier) reach the same USB audio route through Apple's Lightning to USB 3
Camera Adapter, which also passes charging power. Bluetooth LE works on all of them.</p>
<p><b>Power from the phone:</b> an iPhone's USB-C port can power a small accessory (Apple quotes 4.5 W for charging other
devices). Budget well under that, and expect the phone to cut an accessory that draws too much at connection.</p>
""")

    # 3. Bluetooth reference.
    parts.append(f"""
<h2 class="pagebreak">A Bluetooth sensor node</h2>
<p>A single-cell lithium battery, a certified radio module, a quiet regulator and the sensors on one bus. Use a
<b>pre-certified BLE module</b> (nRF52840 or ESP32-S3 class) rather than a bare radio chip: the module's certification
covers the transmitter, which is the expensive part of getting a product to market.</p>
{figure(fig_ble_block(), "Reference architecture. A fuel gauge lets the node report its own battery, which the format "
        "records because a low battery is a known cause of false readings on many meters.")}
<h3>Power</h3>
{figure(fig_power_schematic(), "Charging from USB-C as a sink (5.1 kΩ on each CC pin), a single-cell linear charger, "
        "and a low-noise LDO. A linear regulator wastes a little battery but adds no switching noise next to the sensors.")}
<ul>
<li><b>Battery life target: five hours or more.</b> The phone's own recording is designed to last an investigation of
that length, and a sensor that dies first leaves a gap that reads like silence.</li>
<li><b>Prefer an LDO near magnetic sensors.</b> If a switching regulator is unavoidable, put it at the far end of the
board, keep its inductor's loop small, and measure the node's noise floor with the regulator running.</li>
<li><b>Report the battery honestly</b> over the standard Battery service, and the time since power-on: several
instruments drift until warm, and the format records both for that reason.</li>
</ul>
<h3>The sensor bus</h3>
{figure(fig_i2c_schematic(), "One I²C bus with 4.7 kΩ pull-ups and 100 nF on every part. A data-ready or threshold "
        "interrupt lets the radio sleep until there is something worth sending.")}
""")

    # 4. Over the air.
    parts.append(f"""
<h2 class="pagebreak">Over the air: the contract</h2>
<p>Use the standard Bluetooth services wherever one fits — any BLE tool can read them, which makes bring-up and
debugging much easier — and one IsHaunted service for everything else.</p>
{table(["Service", "UUID", "Characteristics", "Access", "Becomes"], [(a, f'<span class="mono uuid">{b}</span>', c, d, e2) for a, b, c, d, e2 in GATT_ROWS])}
<h3>The IsHaunted Sensor service</h3>
{table(["Characteristic", "UUID", "Access", "Carries"], [(a, f'<span class="mono uuid">{b}</span>', c, d) for a, b, c, d in CUSTOM_ROWS])}
<p>Service <span class="mono">{SERVICE_UUID}</span> is assigned to this project. Advertise it, so the phone can list
only IsHaunted-compatible sensors when someone pairs one.</p>
<h3>A reading on the wire</h3>
{figure(fig_packet(), "The Readings characteristic. A sequence number lets the phone tell a missed sample from a quiet "
        "one; the device time places each sample exactly, however late the radio delivered it.")}
<h3>The Channels descriptor</h3>
<p>Read once at connection. It is what turns channel 3 and an integer into "1.24 µT, ±0.05":</p>
<pre><code>{{
  "format": 1,
  "channels": [
    {{ "id": 1, "name": "emf_x", "unit": "uT", "scale": -2, "accuracy": 0.05, "range": [-800, 800] }},
    {{ "id": 2, "name": "emf_y", "unit": "uT", "scale": -2, "accuracy": 0.05, "range": [-800, 800] }},
    {{ "id": 3, "name": "emf_z", "unit": "uT", "scale": -2, "accuracy": 0.05, "range": [-800, 800] }},
    {{ "id": 4, "name": "temperature", "unit": "degC", "scale": -2, "accuracy": 0.2 }},
    {{ "id": 5, "name": "elf_field", "unit": "uT", "scale": -3 }},
    {{ "id": 6, "name": "elf_frequency", "unit": "Hz", "scale": 0 }}
  ],
  "trigger": {{ "mode": "hybrid", "interval_seconds": 2, "event_description": "field exceeds 2.0 uT above base" }}
}}</code></pre>
<p>Names follow the format's conventions — lowercase, <span class="mono">snake_case</span>, ASCII units
(<span class="mono">uT</span>, not µT). A channel the platform has never seen is still recorded and kept; it is drawn
once someone adds a gauge for it, so a new instrument is never blocked waiting on the app.</p>
<h3>Connection settings Apple asks for</h3>
<ul>
<li><b>Connection interval:</b> at least 15 ms. Apple's accessory guidelines set the exact rules (minimums, multiples,
latency and supervision timeout) — design to the current edition of <i>Accessory Design Guidelines for Apple
Devices</i>, Bluetooth chapter.</li>
<li><b>Advertising:</b> fast (about 20 ms) for the first 30 seconds after power-on or disconnect, then one of the
longer intervals Apple lists, to save battery.</li>
<li><b>Packet size:</b> design so the header and two channels fit 20 bytes; if the phone negotiates a larger MTU, send
more channels per notification.</li>
<li><b>Pairing:</b> "Just Works" is fine for a measuring instrument; nothing secret crosses this link.</li>
</ul>
""")

    # 5. Time.
    parts.append(f"""
<h2>Time is the whole point</h2>
<p>An investigation is read by lining things up: the field jumped at the moment the recording caught a knock. That only
works if every reading carries the right time. The node keeps its own millisecond counter; the phone works out how that
counter maps to its clock.</p>
{figure(fig_time_sync(), "Bluetooth delivers notifications late by a varying amount. The least-delayed one in a window "
        "gives the true offset; tracking it over the session corrects the node's crystal drift.")}
<ul>
<li><b>Stamp at the moment of measurement</b>, not when the packet is sent.</li>
<li><b>Never reset the counter during a session.</b> A wrap of a 32-bit millisecond counter takes 49 days; a reset
mid-session is what breaks alignment.</li>
<li><b>Buffer through a dropped connection</b> and send the backlog afterwards with its original times, flagged as
buffered. A reading that arrives late but correctly timed is worth exactly as much as one that arrived on time.</li>
<li><b>Say how precise you are.</b> The format records a precision for each reading, so a reviewer never reads
false accuracy into a timestamp. Aim for better than 50 ms against the phone's clock.</li>
</ul>
""")

    # 6. Sensors.
    parts.append(f"""
<h2>Sensors worth building</h2>
<p>What investigators reach for, the parts that do it well, and the channel name each should report as. Part numbers
are starting points from widely used families — check current datasheets for noise and range before committing.</p>
{table(["Measures", "Channel", "Unit", "Candidate parts", "Bus", "Notes"], SENSORS, "long")}
<h3>The ELF front end</h3>
{figure(fig_elf_front_end(), "A pick-up coil, an instrumentation amplifier and a band-pass. Over Bluetooth, send the RMS "
        "level and the strongest frequency a few times a second; over USB audio, send the waveform itself.")}
<div class="warn"><p><b>High voltage.</b> Geiger-Müller tubes run at around 400 V. Build that supply with creepage
distances, a bleeder, a current-limited converter and an enclosure that cannot be opened while charged — or use a
PIN-diode radiation module and avoid high voltage entirely.</p></div>
<div class="warn"><p><b>Transmitters.</b> A radar presence sensor, and the node's own radio, are sources of the very
fields an EMF channel measures. Let the Control characteristic turn the radar off, and measure the node's own
signature so it can be told apart.</p></div>
""")

    # 7. Placement and noise.
    parts.append(f"""
<h2>Placement and noise</h2>
<p>A magnetic instrument mostly measures whatever is closest to it. The phone, the node's battery, its regulator and
its radio are all closer than the room.</p>
{figure(fig_placement_sketch(), "Sketch: keep the probe on a non-magnetic stalk, the battery and radio at the handle, and "
        "nothing magnetic near the phone.")}
<ul>
<li><b>Measure the node's own floor</b> in a quiet place, radio transmitting, regulator running: that number belongs
in the Status characteristic and in the instrument's documentation.</li>
<li><b>Calibrate magnetometers per unit</b> — hard-iron offsets and soft-iron scale — and store the result on the
node with its date. The phone records it with every session.</li>
<li><b>Baseline is the investigators' job</b>, and the Control characteristic is how they do it: "set base level" on
the phone asks every connected sensor to take its own.</li>
<li><b>Mark a pegged sensor as out of range</b> rather than sending its maximum. The format has a field for it,
because a clipped maximum read as a measurement is a false finding.</li>
</ul>
{figure(fig_enclosure_sketch(), "Sketch: a hand-held node in four layers. Nylon or brass hardware; the battery held "
        "away from the magnetometer end.")}
""")


    # 7b. The case.
    parts.append(f"""
<h2 class="pagebreak">A sensor case for iPhone and iPad</h2>
<p>One idea under consideration: a case that plugs into the phone's USB-C port like a hub, with ports along its sides
for sensor modules, so an investigator clips on the instruments the night needs. The same idea works for an iPad, with
more room and different hazards.</p>
{figure(fig_case_sketch(), "Sketch: the hub and its processor live in the chin, modules plug into the sides, and the "
        "case's own magnetometer sits in the corner farthest from all of them — and from the phone's own magnets.")}
<h3>How the case reaches the phone</h3>
<p>The modules' bus cannot be handed to iOS as it is; a small processor in the case reads every module and speaks to
the phone as one device, by one of the routes under <i>What the phone will accept</i>:</p>
<div class="cols">
  <div class="card amber"><h4>All wired</h4><p>The case is a USB composite device: a USB network interface for the
  readings, and optionally a USB audio interface for a waveform channel. One cable, no radio — but the network route
  must be proven on an iPhone early.</p></div>
  <div class="card cyan"><h4>Wired power, Bluetooth data</h4><p>USB only powers the case and passes charging through;
  readings go over Bluetooth LE exactly as from a battery node. It depends on nothing iOS might refuse, and the same
  firmware serves the case and a stand-alone node.</p></div>
</div>
{figure(fig_case_block(), "Case electronics. An I²C multiplexer gives each port its own bus, so two modules built "
        "around the same part — and the same address — can be fitted at once.")}
<h3>Modules that describe themselves</h3>
<p>Each module carries a small EEPROM holding its Channels entry (name, unit, scale, accuracy), its serial and its
calibration. The case reads it when a module is plugged in and adds those channels to what it reports, so a new module
needs no new case firmware. A suggested port:</p>
{table(["Pin", "Signal", "Notes"], [
    ["1", "3V3", "From the case, current-limited per port (a load switch with a fault flag)"],
    ["2", "GND", "Make first, break last: the longest pin"],
    ["3", "SDA", "Through the multiplexer; the module has no pull-ups of its own"],
    ["4", "SCL", "As SDA"],
    ["5", "INT", "Open-drain, shared: data ready or threshold crossed"],
    ["6", "DETECT", "Pulled low by the module, so the case sees it arrive and leave"],
])}
<h3>The magnetometer in the case</h3>
<ul>
<li><b>The phone is a magnet.</b> Speakers, the vibration motor, the camera's stabilizer and the MagSafe ring all
contain permanent magnets. Their steady field is removed by calibrating the case magnetometer <b>with the phone
fitted</b> — and recalibrating if the phone changes.</li>
<li><b>What calibration cannot remove</b> is what changes: the speaker playing, the motor buzzing, the phone
charging. Record those moments (the phone knows when it is playing sound) so a reviewer can set them aside.</li>
<li><b>An iPad's edges are lined with magnets</b> for the Pencil and the keyboard. Put the magnetometer top center,
clear of the edges, and do not design an iPad case that holds a Pencil magnetically.</li>
<li><b>Keep the hub's switching parts in the chin,</b> the modules' current away from the magnetometer corner, and
measure the case's floor with every port loaded. If that floor is too high, a fold-out arm puts the magnetometer
farther away still.</li>
</ul>
<h3>If the case is 3D printed</h3>
<p>Printing is the right way to iterate on a case: a new revision in a day, and every phone model is a new file rather
than a new tool. What changes for an investigation case:</p>
{figure(fig_print_section(), "Sketch: a flexible TPU bumper that grips the phone, a rigid shell that carries the "
        "electronics, brass inserts for the board, and the magnetometer in a pocket of its own at the far end.")}
{table(["Material", "Use it for", "Watch out for"], [
    ["<b>PETG</b>", "The rigid shell and module housings", "Strong and slightly flexible; prints easily. Softens around 75 °C"],
    ["<b>ASA</b>", "The shell, for a case that lives in a car", "UV- and heat-resistant (around 95 °C); needs an enclosed printer"],
    ["<b>TPU</b> (95A)", "The bumper that grips the phone", "Flexible; slow to print. Print the bumper separately, or as a second material"],
    ["<b>Nylon</b> (SLS / MJF, PA12)", "A production run from a print service", "Tough, even, no layer lines; the step after FDM prototypes"],
    ["<b>PLA</b>", "Fit checks only", "Too brittle for clips, and softens around 55 °C — a dashboard in summer"],
    ["<b>Iron- or steel-filled</b>", "Nothing", "Never: it is magnetic, and turns the case into part of every reading"],
])}
<ul>
<li><b>Carbon-fiber-filled filaments are not magnetic</b>, so they are safe near the magnetometer, but they are
slightly conductive and dull a Bluetooth antenna placed against them. Keep the radio's antenna end in plain plastic.</li>
<li><b>Fit:</b> work from Apple's published dimensional drawings for each phone and iPad (in the Accessory Design
Guidelines), not from measuring one phone. Allow about 0.2–0.3 mm clearance per side for the rigid shell and let
the TPU bumper do the gripping; print a fit test of just the corners before a whole case.</li>
<li><b>Walls and clips:</b> 2–2.4 mm walls for the shell. Print so layer lines run <i>along</i> a snap-fit clip, not
across it, or it snaps at the first fitting.</li>
<li><b>Fasteners:</b> heat-set <b>brass</b> inserts and brass or nylon screws. A steel screw beside the magnetometer is
a hard-iron offset that changes when it is tightened.</li>
<li><b>Port contacts:</b> spring-loaded pogo pins usually have steel springs. Use ones with non-magnetic (beryllium
copper) springs on any port near the magnetometer, and put a TVS diode on every exposed contact: a plastic case gives
static discharge nowhere else to go.</li>
<li><b>No magnets</b> to hold modules on. A mechanical latch or a printed snap does the same job without adding a field.</li>
<li><b>Openings:</b> the phone's microphones, speakers, camera, flash and buttons stay fully open. A case over a
microphone muffles every recording made through it — exactly the evidence the platform exists to keep.</li>
<li><b>Heat:</b> a phone recording video for hours runs warm, and plastic insulates it. Leave the back over the phone's
hottest area open or vented, and keep the battery bay away from it.</li>
</ul>
<h3>Power</h3>
<p>Pass-through charging is what makes the case usable for a whole night: a USB-C input on the chin feeds a
power-delivery controller that charges the phone and powers the modules. Without a charger, the modules run from the
phone's port, within its small power budget — or from a cell in the case, which also keeps the phone's battery for the
recording itself.</p>
""")

    # 8. Journey + acceptance.
    parts.append(f"""
<h2 class="pagebreak">From a sample to the player</h2>
{figure(fig_data_journey(), "The builder's part ends when the phone has the packet. From there a reading is stored in the "
        "session, uploaded with it, and drawn on the same timeline as the recordings and the map.")}
<p>Inside the session, a reading from your node looks like this — one more key under
<span class="mono">measurements</span>, beside what the phone measured itself:</p>
<pre><code>{{
  "at": "2026-10-31T23:14:07.240Z",
  "precision": "millisecond",
  "sequence": 5812,
  "triggered_by": "event",
  "measurements": {{
    "emf":           {{ "value": 0.62, "unit": "uT", "baseline": 0.31 }},
    "probe_emf":     {{ "value": 1.24, "unit": "uT", "accuracy": 0.05, "baseline": 0.40 }},
    "elf_field":     {{ "value": 0.180, "unit": "uT" }},
    "elf_frequency": {{ "value": 60, "unit": "Hz" }},
    "temperature":   {{ "value": 17.42, "unit": "degC", "accuracy": 0.2 }}
  }}
}}</code></pre>
<p>The rules that make this safe to extend: every number carries a unit; everything but the time is optional; a reader
ignores channels it does not know rather than rejecting the file. A new instrument is an additive change — the format's
minor version moves, nothing breaks.</p>

<h2>Before a prototype comes to a test night</h2>
<ul class="check">
<li>Advertises the IsHaunted service and answers Device Information with real names and a firmware version.</li>
<li>Channels descriptor reads back valid JSON with a unit on every numeric channel.</li>
<li>Sequence numbers never repeat and never skip, except across a genuine loss.</li>
<li>Survives walking out of range and back: buffered readings arrive with their original times.</li>
<li>Runs five hours from a full charge with the radio connected.</li>
<li>Noise floor measured with the radio and regulator running, and written down.</li>
<li>Warm-up drift measured over the first 30 minutes after power-on.</li>
<li>Reports out-of-range rather than a clipped maximum.</li>
<li>Nothing magnetic in the enclosure, and no MagSafe mount on a magnetic sensor.</li>
<li>For high voltage: enclosure closed, supply current-limited, bleeder fitted.</li>
</ul>

<h2>What to send us with a prototype</h2>
<p>The Channels descriptor, the GATT table as built, a capture of a few minutes of notifications (any BLE sniffer or
phone tool), the measured noise floor and battery life, and the calibration procedure. With those, the phone's side can
be built and tested against your instrument rather than an idea of it.</p>
""")

    parts.append("</body></html>")
    with open(html_out, "w", encoding="utf-8") as f:
        f.write("".join(parts))
    subprocess.run([CHROME, "--headless", "--no-pdf-header-footer", "--disable-gpu",
                    f"--print-to-pdf={pdf_out}", "file://" + html_out], check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print(pdf_out)


if __name__ == "__main__":
    for edition in EDITIONS:
        build(edition)
