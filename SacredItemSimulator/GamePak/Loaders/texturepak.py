import struct
import zlib
import pygame
import sys
import os
import re
import tkinter as tk
from tkinter import filedialog

# ---------- DECOMPRESSION / CONVERSION ----------

def decompress_rle(compressed, width, height, stride):
    #based on FUN_004d46b0
    src = memoryview(bytearray(compressed))
    src_len = len(src)
    dst = [0] * (width * height)
    bytes_written = 0
    max_bytes = width * height * 2
    row_gap_bytes = stride - width * 2
    simple_mode = (row_gap_bytes == 0)
    src_pos = 0
    dst_pos = 0
    col = 0

    def read_length(base_ctrl, pos):
        length = base_ctrl & 0x7F
        if length == 0x7F:
            if pos + 2 > src_len:
                return 0, pos
            length = src[pos] | (src[pos + 1] << 8)
            pos += 2
        return length, pos

    while dst_pos < len(dst) and src_pos < src_len:
        ctrl = src[src_pos]
        src_pos += 1
        length, src_pos = read_length(ctrl, src_pos)
        bytes_written += length * 2
        if bytes_written > max_bytes or length == 0:
            break
        is_repeat = bool(ctrl & 0x80)

        if is_repeat:
            if src_pos + 2 > src_len:
                break
            val = src[src_pos] | (src[src_pos + 1] << 8)
            src_pos += 2
            if simple_mode:
                end = min(dst_pos + length, len(dst))
                for i in range(dst_pos, end):
                    dst[i] = val
                dst_pos = end
            else:
                remaining = length
                while remaining > 0 and dst_pos < len(dst):
                    in_row = width - col
                    now = min(remaining, in_row)
                    for i in range(dst_pos, min(dst_pos + now, len(dst))):
                        dst[i] = val
                    dst_pos += now
                    col += now
                    remaining -= now
                    if col >= width:
                        col = 0
        else:
            if simple_mode:
                for _ in range(length):
                    if src_pos + 2 > src_len or dst_pos >= len(dst):
                        break
                    dst[dst_pos] = src[src_pos] | (src[src_pos + 1] << 8)
                    dst_pos += 1
                    src_pos += 2
            else:
                remaining = length
                while remaining > 0 and dst_pos < len(dst) and src_pos + 2 <= src_len:
                    in_row = width - col
                    now = min(remaining, in_row)
                    for _ in range(now):
                        if src_pos + 2 > src_len or dst_pos >= len(dst):
                            break
                        dst[dst_pos] = src[src_pos] | (src[src_pos + 1] << 8)
                        dst_pos += 1
                        src_pos += 2
                        col += 1
                    remaining -= now
                    if col >= width:
                        col = 0
    return dst

def decode_4444(data, width, height):
    if isinstance(data, (bytes, bytearray, memoryview)):
        pixels = []
        for i in range(0, len(data) - (len(data) % 2), 2):
            pixels.append(struct.unpack_from("<H", data, i)[0])
    else:
        pixels = data

    surf = pygame.Surface((width, height), pygame.SRCALPHA)
    max_pixels = width * height
    for i, v in enumerate(pixels):
        if i >= max_pixels:
            break
        x = i % width
        y = i // width
        surf.set_at((x, y), (
            ((v >> 8) & 0xF) * 17,
            ((v >> 4) & 0xF) * 17,
            (v & 0xF) * 17,
            ((v >> 12) & 0xF) * 17,
        ))
    return surf

def surface_to_type6(surface):
    if surface.get_bytesize() != 4:
        surface = surface.convert_alpha()
    rgba = pygame.image.tostring(surface, "RGBA")
    out = bytearray()
    for i in range(0, len(rgba), 4):
        r = rgba[i]
        g = rgba[i+1]
        b = rgba[i+2]
        a = rgba[i+3]
        out.extend([b, g, r, a])
    return bytes(out)

def surface_to_type0(surface):
    if surface.get_bytesize() != 4:
        surface = surface.convert_alpha()
    raw = pygame.image.tostring(surface, "RGBA")
    out = bytearray()
    for i in range(0, len(raw), 4):
        r = raw[i]
        g = raw[i+1]
        b = raw[i+2]
        a = raw[i+3]
        r4 = r // 17
        g4 = g // 17
        b4 = b // 17
        a4 = a // 17
        val = (a4 << 12) | (r4 << 8) | (g4 << 4) | b4
        out.extend(struct.pack('<H', val))
    return bytes(out)

# ---------- Archive modification ----------

def replace_texture_in_archive(archive_path, tex_index, new_data, new_type, original_tex):
    try:
        with open(archive_path, 'r+b') as f:
            f.seek(4)
            nb, unk = struct.unpack('<II', f.read(8))
            f.seek(244, 1)
            entries = []
            for _ in range(nb):
                unk2, off, size = struct.unpack('<III', f.read(12))
                entries.append([unk2, off, size])

            if tex_index >= len(entries):
                return False, None, None

            old_off, old_size = entries[tex_index][1], entries[tex_index][2]
            new_size = len(new_data)
            new_offset = old_off

            if new_size <= old_size:
                f.seek(old_off + 36)
                f.write(struct.pack('<B', new_type))
                f.seek(old_off + 80)
                f.write(new_data)
            else:
                f.seek(0, 2)
                new_offset = f.tell()
                f.seek(old_off)
                name_bytes = f.read(32)
                w_h = f.read(4)
                f.read(1)
                rest_of_header = f.read(4 + 39)
                header = name_bytes + w_h + struct.pack('<B', new_type) + rest_of_header
                f.seek(new_offset)
                f.write(header)
                f.write(new_data)

            entries[tex_index][1] = new_offset
            entries[tex_index][2] = new_size

            f.seek(0)
            data_before_entries = f.read(4 + 8 + 244)
            f.seek(0)
            f.write(data_before_entries)
            for unk2, off, sz in entries:
                f.write(struct.pack('<III', unk2, off, sz))
        return True, new_offset, new_size
    except Exception as e:
        print(f"Replace error: {e}")
        return False, None, None

# ---------- Helpers ----------

def clamp(v, a, b):
    return max(a, min(v, b))

def read_struct(fmt, f):
    return struct.unpack(fmt, f.read(struct.calcsize(fmt)))

def sanitize_filename(name):
    return re.sub(r'[\\/*?:"<>|]', "_", name)

def surface_to_tga(surface):
    w, h = surface.get_size()
    has_alpha = surface.get_bytesize() == 4
    descriptor = 0x20 | (8 if has_alpha else 0)
    header = struct.pack('<BBB', 0, 0, 2) + b'\x00' * 5
    header += struct.pack('<HHHHBB', 0, 0, w, h, 32 if has_alpha else 24, descriptor)
    if has_alpha:
        raw = pygame.image.tostring(surface, 'RGBA')
        data = bytearray(raw)
        for i in range(0, len(data), 4):
            data[i], data[i + 2] = data[i + 2], data[i]
    else:
        raw = pygame.image.tostring(surface, 'RGB')
        data = bytearray(raw)
        for i in range(0, len(data), 3):
            data[i], data[i + 2] = data[i + 2], data[i]
    return header + bytes(data)

def save_surface(surf, path, fmt):
    fmt = fmt.lower()
    if fmt == 'tga':
        with open(path, 'wb') as f:
            f.write(surface_to_tga(surf))
    else:
        pygame.image.save(surf, path)

def default_tex_name(tex, idx):
    return sanitize_filename(tex.name if tex.name else f"tex_{idx}")

# ---------- Pygame UI Components ----------

class Button:
    def __init__(self, rect, text, callback, colors=((58,58,145),(100,100,200),(40,40,80)),
                 text_color=(225,225,225), font=None):
        self.rect = pygame.Rect(rect)
        self.text = text
        self.callback = callback
        self.colors = colors
        self.text_color = text_color
        self.font = font or pygame.font.SysFont("consolas", 15)
        self.hover = False
        self.pressed = False

    def handle_event(self, event):
        consumed = False
        if event.type == pygame.MOUSEMOTION:
            self.hover = self.rect.collidepoint(event.pos)
        elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if self.hover:
                self.pressed = True
                consumed = True
        elif event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            if self.pressed and self.hover:
                self.callback()
                consumed = True
            self.pressed = False
        return consumed

    def draw(self, screen):
        if self.pressed:
            color = self.colors[2]
        elif self.hover:
            color = self.colors[1]
        else:
            color = self.colors[0]
        pygame.draw.rect(screen, color, self.rect, border_radius=4)
        text_surf = self.font.render(self.text, True, self.text_color)
        text_rect = text_surf.get_rect(center=self.rect.center)
        screen.blit(text_surf, text_rect)


class MenuButton(Button):
    def __init__(self, rect, text, option_callbacks, option_labels,
                 colors=((58,58,145),(100,100,200),(40,40,80)), font=None):
        super().__init__(rect, text, lambda: None, colors, font=font)
        self.option_callbacks = option_callbacks
        self.option_labels = option_labels
        self.active = False
        self.option_buttons = []

    def _rebuild_options(self):
        if not self.active:
            self.option_buttons = []
            return
        opt_height = self.rect.height - 4
        total_height = len(self.option_labels) * (opt_height + 2)
        y = self.rect.top - total_height - 2
        self.option_buttons = []
        for i, (label, cb) in enumerate(zip(self.option_labels, self.option_callbacks)):
            btn_rect = pygame.Rect(self.rect.left, y + i * (opt_height + 2),
                                   self.rect.width, opt_height)
            self.option_buttons.append(Button(btn_rect, label, cb, self.colors,
                                              self.text_color, self.font))

    def set_active(self, active):
        if self.active == active:
            return
        self.active = active
        if active:
            self._rebuild_options()
        else:
            self.option_buttons = []

    def handle_event(self, event):
        consumed = False
        if self.active:
            for btn in self.option_buttons:
                if btn.handle_event(event):
                    consumed = True
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                if not any(btn.rect.collidepoint(event.pos) for btn in self.option_buttons) \
                   and not self.rect.collidepoint(event.pos):
                    self.set_active(False)
                    consumed = True
        if event.type == pygame.MOUSEMOTION:
            self.hover = self.rect.collidepoint(event.pos)
        elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if self.hover:
                self.pressed = True
                consumed = True
        elif event.type == pygame.MOUSEBUTTONUP and event.button == 1:
            if self.pressed and self.hover:
                self.set_active(not self.active)
                consumed = True
            self.pressed = False
        return consumed

    def update_options_position(self):
        if not self.active:
            return
        opt_height = self.rect.height - 4
        total_height = len(self.option_labels) * (opt_height + 2)
        y = self.rect.top - total_height - 2
        for i, btn in enumerate(self.option_buttons):
            btn.rect = pygame.Rect(self.rect.left, y + i * (opt_height + 2),
                                   self.rect.width, opt_height)

    def draw(self, screen):
        super().draw(screen)
        for btn in self.option_buttons:
            btn.draw(screen)


class ProgressDialog:
    def __init__(self, title, total, font=None):
        self.title = title
        self.total = total
        self.current = 0
        self.cancelled = False
        self.active = False
        self.font = font or pygame.font.SysFont("consolas", 15)
        self.rect = None
        self.cancel_btn = None

    def open(self, screen_rect):
        self.active = True
        self.cancelled = False
        self.current = 0
        w = 400
        h = 120
        x = (screen_rect.width - w) // 2
        y = (screen_rect.height - h) // 2
        self.rect = pygame.Rect(x, y, w, h)
        self.cancel_btn = Button(pygame.Rect(self.rect.centerx - 40,
                                             self.rect.bottom - 35, 80, 25),
                                 "Cancel", self.cancel, font=self.font)

    def cancel(self):
        self.cancelled = True

    def update(self, current):
        self.current = current

    def handle_event(self, event):
        if self.active and self.cancel_btn:
            self.cancel_btn.handle_event(event)

    def draw(self, screen):
        if not self.active:
            return
        dim = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        dim.fill((0,0,0,200))
        screen.blit(dim, (0,0))
        pygame.draw.rect(screen, (40,40,60), self.rect, border_radius=8)
        pygame.draw.rect(screen, (120,120,150), self.rect, 2, border_radius=8)
        title_surf = self.font.render(self.title, True, (255,255,200))
        screen.blit(title_surf, (self.rect.x + 20, self.rect.y + 15))
        bar_rect = pygame.Rect(self.rect.x + 20, self.rect.y + 45,
                               self.rect.width - 40, 20)
        pygame.draw.rect(screen, (20,20,30), bar_rect)
        if self.total > 0:
            fill_w = int(bar_rect.width * (self.current / self.total))
            pygame.draw.rect(screen, (80,180,80),
                             pygame.Rect(bar_rect.x, bar_rect.y, fill_w, bar_rect.height))
        text = f"{self.current} / {self.total}"
        txt_surf = self.font.render(text, True, (220,220,220))
        screen.blit(txt_surf, (bar_rect.x + 5, bar_rect.y - 18))
        self.cancel_btn.draw(screen)


# ---------- Texture + Archive ----------

class Tex:
    def __init__(self, idx, name, offset, size, w, h, t):
        self.id = idx
        self.name = name
        self.offset = offset
        self.size = size
        self.w = w
        self.h = h
        self.type = t
        self.surface = None

class Archive:
    def __init__(self, path):
        self.path = path
        self.f = open(path, "rb")
        self.textures = []
        self._index()

    def _index(self):
        try:
            self.f.read(4)
            nb, _ = read_struct("<II", self.f)
            self.f.read(244)
            entries = [read_struct("<III", self.f) for _ in range(nb)]
            for i, (_, off, size) in enumerate(entries):
                self.f.seek(off if off > 0 else 0)
                name = self.f.read(32).split(b"\x00")[0].decode(errors="ignore") if off > 0 else ""
                w, h = read_struct("<HH", self.f) if off > 0 else (0, 0)
                t = struct.unpack("<B", self.f.read(1))[0] if off > 0 else 0
                if off > 0:
                    self.f.read(4)
                    self.f.read(39)
                self.textures.append(Tex(i, name, off, size, w, h, t))
        except Exception as e:
            print(f"Index error: {e}")

    def load(self, idx):
        t = self.textures[idx]
        if t.surface:
            return t.surface
        if t.offset <= 0 or t.size <= 0 or t.w <= 0 or t.h <= 0:
            return None
        self.f.seek(t.offset)
        self.f.read(32)
        read_struct("<HH", self.f)
        self.f.read(1)
        read_struct("<I", self.f)
        self.f.read(39)
        data = self.f.read(t.size)

        if t.type == 6:
            surf = pygame.Surface((t.w, t.h), pygame.SRCALPHA)
            i = 0
            for y in range(t.h):
                for x in range(t.w):
                    if i + 4 > len(data):
                        break
                    v = struct.unpack_from("<I", data, i)[0]
                    i += 4
                    surf.set_at((x, y), ((v>>16)&255, (v>>8)&255, v&255, (v>>24)&255))
            t.surface = surf
            return surf

        if t.type == 4:
            try:
                dec = zlib.decompress(data)
            except Exception:
                return None
            t.surface = decode_4444(dec, t.w, t.h)
            return t.surface

        if t.type == 3:
            pixels = decompress_rle(data, t.w, t.h, t.w * 2)
            t.surface = decode_4444(pixels, t.w, t.h)
            return t.surface

        if t.type == 0:
            t.surface = decode_4444(data, t.w, t.h)
            return t.surface

        return None

    def close(self):
        self.f.close()


# ---------- UI State ----------

class UI:
    def __init__(self):
        self.zoom = 1.0
        self.pan_x = 0
        self.pan_y = 0
        self.drag = False
        self.scroll = 0
        self.scroll_drag = False
        self.scroll_grab_y = 0
        self.searching = False
        self.search_text = ""
        self.show_broken = False
        self.message = None
        self.message_until = 0
        self._cached_search = ""
        self._cached_broken = False
        self._cached_archive = None
        self.display_list = []

    def update_display_list(self, archive):
        if (archive is self._cached_archive and
            self.search_text == self._cached_search and
            self.show_broken == self._cached_broken):
            return
        self._cached_archive = archive
        self._cached_search = self.search_text
        self._cached_broken = self.show_broken

        if archive is None:
            self.display_list = []
            return

        s = self.search_text.lower()
        out = []
        for i, tex in enumerate(archive.textures):
            valid = tex.offset > 0 and tex.size > 0 and tex.w > 0 and tex.h > 0
            if not self.show_broken and not valid:
                continue
            if s and s not in tex.name.lower() and s not in str(tex.id):
                continue
            out.append(i)
        self.display_list = out

def scroll_to(ui, display_idx, item_h, list_h, total):
    t = display_idx * item_h - list_h // 2
    ui.scroll = clamp(t, 0, max(0, total * item_h - list_h))


# ---------- Main ----------

def run():
    pygame.init()
    screen = pygame.display.set_mode((1280, 720), pygame.RESIZABLE)
    pygame.display.set_caption("PAK Texture Viewer")
    clock = pygame.time.Clock()
    font = pygame.font.SysFont("consolas", 15)
    fontS = pygame.font.SysFont("consolas", 13)

    # --- Native file dialogs setup ---
    # Hide the root tkinter window (only needed for dialogs)
    tk_root = tk.Tk()
    tk_root.withdraw()

    archive = None
    ui = UI()
    selected = -1
    last_win_size = (0, 0)
    active_dialog = None
    export_generator = None

    # Layout constants
    LIST_W = 430
    SB_W = 12
    ITEM_H = 20
    SRCH_H = 28
    BTN_H = 36

    # Colors
    C_BG         = (20, 20, 26)
    C_PANEL      = (30, 30, 38)
    C_BORDER     = (52, 52, 66)
    C_SEL_BG     = (52, 46, 18)
    C_ITEM       = (200, 200, 210)
    C_ITEM_BRK   = (110, 110, 125)
    C_ITEM_SEL   = (255, 210, 80)
    C_SB_BG      = (40, 40, 50)
    C_SB_THUMB   = (100, 100, 130)
    C_SB_HOT     = (155, 155, 195)
    C_BTN_TXT    = (225, 225, 225)
    C_SRCH_BG    = (38, 38, 50)
    C_SRCH_ACT   = (48, 48, 65)
    C_INFO_BG    = (12, 12, 18, 190)
    C_INFO_TXT   = (220, 220, 220)
    C_MSG        = (240, 205, 70)
    C_HINT       = (62, 62, 78)

    # Helper functions
    def show_message(text, is_error=False):
        ui.message = text
        ui.message_until = pygame.time.get_ticks() + 3000
        if is_error:
            print("Error:", text)

    def export_one(archive, idx, path, fmt):
        surf = archive.load(idx)
        if not surf:
            return False, "decode failed"
        try:
            save_surface(surf, path, fmt)
            return True, None
        except Exception as ex:
            return False, str(ex)

    def replace_texture_with_file(img_path, new_type):
        nonlocal archive, selected, ui, active_dialog
        try:
            new_surf = pygame.image.load(img_path).convert_alpha()
        except Exception as ex:
            show_message(f"Failed to load image: {ex}", True)
            return
        tex = archive.textures[selected]
        scaled_surf = pygame.transform.scale(new_surf, (tex.w, tex.h))
        if new_type == 0:
            compressed = surface_to_type0(scaled_surf)
        else:
            compressed = surface_to_type6(scaled_surf)
        archive.close()
        success, new_off, new_sz = replace_texture_in_archive(archive.path, selected,
                                                              compressed, new_type, tex)
        if success:
            archive = Archive(archive.path)
            ui.update_display_list(archive)
            selected = selected if selected < len(archive.textures) else -1
            show_message(f"Texture replaced (type {new_type})")
        else:
            show_message("Replace failed!", True)

    # --- Native file dialog helpers (replace FileBrowser) ---
    def open_file_dialog(title, callback):
        path = filedialog.askopenfilename(title=title)
        callback(path)

    def open_save_dialog(title, default_name, callback):
        path = filedialog.asksaveasfilename(title=title, initialfile=default_name, defaultextension=".*")
        callback(path)

    def open_folder_dialog(title, callback):
        path = filedialog.askdirectory(title=title)
        callback(path)

    def start_export_all(folder, fmt, batch_size=50):
        nonlocal export_generator, active_dialog
        dlist = ui.display_list
        total = len(dlist)
        if total == 0:
            show_message("No textures to export.")
            return
        progress = ProgressDialog("Exporting...", total)
        progress.open(screen.get_rect())
        active_dialog = progress

        def gen():
            i = 0
            while i < total:
                if progress.cancelled:
                    break
                # Process a batch of textures before yielding
                end = min(i + batch_size, total)
                for idx in range(i, end):
                    tex_idx = dlist[idx]
                    tex = archive.textures[tex_idx]
                    base = default_tex_name(tex, tex_idx)
                    path = os.path.join(folder, f"{base}.{fmt}")
                    if os.path.exists(path):
                        path = os.path.join(folder, f"{base}_{tex_idx}.{fmt}")
                    export_one(archive, tex_idx, path, fmt)   # ignore success/failure for speed
                i = end
                progress.update(i)
                yield                     # allow UI to update after the batch
            nonlocal active_dialog, export_generator
            if active_dialog is progress:
                active_dialog = None
            if not progress.cancelled:
                show_message(f"Exported {progress.current} textures.")
            export_generator = None

        export_generator = gen()
        try:
            next(export_generator)   # start first batch
        except StopIteration:
            pass

    # Callbacks for menu options
    def on_extract_png():
        if archive is None or selected < 0:
            return
        tex = archive.textures[selected]
        default_name = default_tex_name(tex, selected) + ".png"
        def on_save(path):
            if path:
                ok, err = export_one(archive, selected, path, 'png')
                if ok:
                    show_message(f"Saved {os.path.basename(path)}")
                else:
                    show_message(f"Export failed: {err}", True)
        open_save_dialog("Save PNG", default_name, on_save)

    def on_extract_tga():
        if archive is None or selected < 0:
            return
        tex = archive.textures[selected]
        default_name = default_tex_name(tex, selected) + ".tga"
        def on_save(path):
            if path:
                ok, err = export_one(archive, selected, path, 'tga')
                if ok:
                    show_message(f"Saved {os.path.basename(path)}")
                else:
                    show_message(f"Export failed: {err}", True)
        open_save_dialog("Save TGA", default_name, on_save)

    def on_extract_all_png():
        if archive is None:
            return
        def on_folder(folder):
            if folder:
                start_export_all(folder, 'png')
        open_folder_dialog("Select export folder", on_folder)

    def on_extract_all_tga():
        if archive is None:
            return
        def on_folder(folder):
            if folder:
                start_export_all(folder, 'tga')
        open_folder_dialog("Select export folder", on_folder)

    def on_replace_type0():
        if archive is None or selected < 0:
            return
        def on_image(path):
            if path:
                replace_texture_with_file(path, 0)
        open_file_dialog("Select image file", on_image)

    def on_replace_type6():
        if archive is None or selected < 0:
            return
        def on_image(path):
            if path:
                replace_texture_with_file(path, 6)
        open_file_dialog("Select image file", on_image)

    def toggle_broken():
        ui.show_broken = not ui.show_broken
        ui._cached_broken = not ui.show_broken
        if toggle_btn:
            toggle_btn.text = "Hide Broken" if ui.show_broken else "Show Broken"

    # Create buttons (will be recreated on resize)
    def create_buttons(screen_w, screen_h):
        bw = (LIST_W - 6) // 4
        by = screen_h - BTN_H + 4
        padding = 2
        gap = 2
        btn_rects = [
            pygame.Rect(padding + (bw+gap)*0, by, bw, BTN_H-8),
            pygame.Rect(padding + (bw+gap)*1, by, bw, BTN_H-8),
            pygame.Rect(padding + (bw+gap)*2, by, bw, BTN_H-8),
            pygame.Rect(padding + (bw+gap)*3, by, bw, BTN_H-8),
        ]
        ext = MenuButton(btn_rects[0], "Extract",
                         [on_extract_png, on_extract_tga],
                         ["PNG", "TGA"], font=fontS)
        ext_all = MenuButton(btn_rects[1], "Extract list",
                             [on_extract_all_png, on_extract_all_tga],
                             ["PNG", "TGA"], font=fontS)
        rep = MenuButton(btn_rects[2], "Replace",
                         [on_replace_type0, on_replace_type6],
                         ["Type 0", "Type 6"], font=fontS)
        tog = Button(btn_rects[3], "Hide Broken" if ui.show_broken else "Show Broken",
                     toggle_broken, font=fontS)
        return ext, ext_all, rep, tog

    # Initial button creation
    extract_btn, extract_all_btn, replace_btn, toggle_btn = create_buttons(1280, 720)
    all_buttons = [extract_btn, extract_all_btn, replace_btn, toggle_btn]

    running = True
    while running:
        W, H = screen.get_size()
        mx, my = pygame.mouse.get_pos()
        now = pygame.time.get_ticks()

        # Recreate buttons on resize
        if (W, H) != last_win_size:
            extract_btn, extract_all_btn, replace_btn, toggle_btn = create_buttons(W, H)
            all_buttons = [extract_btn, extract_all_btn, replace_btn, toggle_btn]
            last_win_size = (W, H)

        # Update toggle button text
        toggle_btn.text = "Hide Broken" if ui.show_broken else "Show Broken"

        # Geometry
        list_top = SRCH_H
        list_bot = H - BTN_H
        list_h = list_bot - list_top
        sb_x = LIST_W - SB_W
        prev_w = W - LIST_W

        ui.update_display_list(archive)
        dlist = ui.display_list
        ntotal = len(dlist)
        lpx = ntotal * ITEM_H
        maxscr = max(0, lpx - list_h)
        ui.scroll = clamp(ui.scroll, 0, maxscr)

        if selected not in dlist and dlist:
            selected = dlist[0]
        elif selected >= 0 and not dlist:
            selected = -1

        # Scrollbar
        if lpx > list_h and list_h > 0:
            th_h = max(18, list_h * list_h // lpx)
            th_y = list_top + int((ui.scroll / maxscr) * (list_h - th_h)) if maxscr > 0 else list_top
        else:
            th_h = list_h
            th_y = list_top
        th_rect = pygame.Rect(sb_x, th_y, SB_W, th_h)
        th_hot = th_rect.collidepoint(mx, my) or ui.scroll_drag

        # --- Event handling ---
        for e in pygame.event.get():
            if e.type == pygame.QUIT:
                running = False
                continue

            # Drag & drop
            if e.type == pygame.DROPFILE:
                if archive:
                    archive.close()
                try:
                    archive = Archive(e.file)
                    ui = UI()
                    selected = archive.textures[0].id if archive.textures else -1
                    ui.update_display_list(archive)
                    show_message(f"Loaded {os.path.basename(e.file)}")
                except Exception as ex:
                    archive = None
                    selected = -1
                    show_message(f"Failed to load: {ex}", True)
                continue

            # Modal dialog (progress bar only now)
            if active_dialog:
                active_dialog.handle_event(e)
                continue

            # Buttons first for mouse events
            button_consumed = False
            if e.type in (pygame.MOUSEMOTION, pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
                for btn in all_buttons:
                    if btn.handle_event(e):
                        button_consumed = True
                if button_consumed:
                    continue

            # Default mouse handling
            if e.type == pygame.MOUSEWHEEL:
                if mx < LIST_W:
                    ui.scroll = clamp(ui.scroll - e.y * 40, 0, maxscr)
                elif not ui.searching:
                    ui.zoom = clamp(ui.zoom * (1.12 if e.y > 0 else 0.88), 0.05, 64.0)

            elif e.type == pygame.MOUSEBUTTONDOWN and e.button == 1:
                srch_rect = pygame.Rect(0, 0, sb_x, SRCH_H)
                if srch_rect.collidepoint(mx, my):
                    ui.searching = True
                elif sb_x <= mx < LIST_W and list_top <= my < list_bot:
                    if th_rect.collidepoint(mx, my):
                        ui.scroll_drag = True
                        ui.scroll_grab_y = my - th_y
                    else:
                        if my < th_y:
                            ui.scroll = clamp(ui.scroll - list_h, 0, maxscr)
                        else:
                            ui.scroll = clamp(ui.scroll + list_h, 0, maxscr)
                elif mx < sb_x and list_top <= my < list_bot:
                    ui.searching = False
                    idx_in = int((my - list_top + ui.scroll) // ITEM_H)
                    if 0 <= idx_in < len(dlist):
                        selected = dlist[idx_in]
                        ui.zoom = 1.0
                        ui.pan_x = ui.pan_y = 0
                elif mx >= LIST_W:
                    ui.drag = True
                    ui.searching = False

            elif e.type == pygame.MOUSEBUTTONDOWN and e.button == 3:
                ui.zoom = 1.0
                ui.pan_x = ui.pan_y = 0

            elif e.type == pygame.MOUSEBUTTONUP and e.button == 1:
                ui.drag = False
                ui.scroll_drag = False

            elif e.type == pygame.MOUSEMOTION:
                if ui.drag and mx >= LIST_W:
                    ui.pan_x += e.rel[0]
                    ui.pan_y += e.rel[1]
                if ui.scroll_drag and maxscr > 0:
                    new_ty = my - ui.scroll_grab_y - list_top
                    rng = list_h - th_h
                    if rng > 0:
                        ui.scroll = clamp(int(new_ty / rng * maxscr), 0, maxscr)

            elif e.type == pygame.KEYDOWN:
                if ui.searching:
                    if e.key == pygame.K_ESCAPE:
                        ui.searching = False
                        ui.search_text = ""
                    elif e.key == pygame.K_RETURN:
                        ui.searching = False
                    elif e.key == pygame.K_BACKSPACE:
                        ui.search_text = ui.search_text[:-1]
                    elif e.unicode and e.unicode.isprintable():
                        ui.search_text += e.unicode
                else:
                    if e.key in (pygame.K_SLASH, pygame.K_KP_DIVIDE):
                        ui.searching = True
                    elif e.key == pygame.K_ESCAPE:
                        ui.search_text = ""
                        ui._cached_search = None
                    elif e.key == pygame.K_1:
                        ui.zoom = 1.0
                        ui.pan_x = ui.pan_y = 0
                    elif e.key == pygame.K_f and archive and selected >= 0:
                        surf = archive.load(selected)
                        if surf:
                            sw, sh = surf.get_size()
                            if sw > 0 and sh > 0:
                                ui.zoom = min(prev_w / sw, H / sh)
                    elif e.key == pygame.K_RIGHT and archive and dlist:
                        if selected in dlist:
                            ni = (dlist.index(selected) + 1) % len(dlist)
                            selected = dlist[ni]
                            scroll_to(ui, ni, ITEM_H, list_h, ntotal)
                    elif e.key == pygame.K_LEFT and archive and dlist:
                        if selected in dlist:
                            ni = (dlist.index(selected) - 1) % len(dlist)
                            selected = dlist[ni]
                            scroll_to(ui, ni, ITEM_H, list_h, ntotal)

        if isinstance(active_dialog, ProgressDialog) and export_generator:
            try:
                next(export_generator)
            except StopIteration:
                pass
        # --- Drawing ---
        screen.fill(C_BG)

        # Preview area
        prev_rect = pygame.Rect(LIST_W, 0, prev_w, H)
        if archive and selected >= 0:
            surf = archive.load(selected)
            if surf:
                sw, sh = surf.get_size()
                dw = max(1, int(sw * ui.zoom))
                dh = max(1, int(sh * ui.zoom))
                cx = LIST_W + prev_w // 2 + ui.pan_x
                cy = H // 2 + ui.pan_y
                img_rect = pygame.Rect(cx - dw//2, cy - dh//2, dw, dh)
                clip = img_rect.clip(prev_rect)
                if clip.width > 0 and clip.height > 0:
                    draw_checker(screen, (clip.x, clip.y, clip.width, clip.height))
                old_clip = screen.get_clip()
                screen.set_clip(prev_rect)
                try:
                    scaled = pygame.transform.scale(surf, (dw, dh))
                    screen.blit(scaled, img_rect)
                except Exception:
                    pass
                screen.set_clip(old_clip)

        # Left panel
        pygame.draw.rect(screen, C_PANEL, (0, 0, LIST_W, H))
        pygame.draw.line(screen, C_BORDER, (LIST_W, 0), (LIST_W, H), 2)

        # Search bar
        sb_bg = C_SRCH_ACT if ui.searching else C_SRCH_BG
        pygame.draw.rect(screen, sb_bg, (0, 0, sb_x, SRCH_H))
        pygame.draw.rect(screen, C_BORDER, (0, 0, sb_x, SRCH_H), 1)
        blink = (now // 500) % 2 == 0
        disp = ui.search_text + ("|" if ui.searching and blink else "")
        placeholder = disp if disp else ("Search names / IDs..." if not ui.searching else "")
        sc = C_ITEM if ui.search_text else (80, 80, 100)
        screen.blit(font.render("/ ", True, (90, 90, 115)), (5, 6))
        screen.blit(font.render(placeholder, True, sc), (22, 6))

        # Texture list
        old_clip = screen.get_clip()
        screen.set_clip(pygame.Rect(0, list_top, sb_x, list_h))
        base_y = list_top - ui.scroll
        for i, idx in enumerate(dlist):
            iy = base_y + i * ITEM_H
            if iy + ITEM_H < list_top or iy > list_bot:
                continue
            tex = archive.textures[idx] if archive else None
            valid = (tex.offset > 0 and tex.size > 0 and tex.w > 0 and tex.h > 0) if tex else False
            is_sel = (selected == idx)
            if is_sel:
                pygame.draw.rect(screen, C_SEL_BG, (0, iy, sb_x, ITEM_H))
            col = C_ITEM_SEL if is_sel else (C_ITEM_BRK if not valid else C_ITEM)
            label = f"{idx}: {tex.name}" if tex and tex.name else f"{idx}: <unnamed>"
            screen.blit(fontS.render(label, True, col), (5, iy + 3))
        screen.set_clip(old_clip)

        # List borders & scrollbar
        pygame.draw.line(screen, C_BORDER, (0, list_top - 1), (LIST_W, list_top - 1))
        pygame.draw.line(screen, C_BORDER, (0, list_bot), (LIST_W, list_bot))
        pygame.draw.rect(screen, C_SB_BG, (sb_x, list_top, SB_W, list_h))
        pygame.draw.rect(screen, C_SB_HOT if th_hot else C_SB_THUMB, th_rect)

        # Bottom buttons
        pygame.draw.rect(screen, C_PANEL, (0, list_bot, LIST_W, BTN_H))
        extract_btn.draw(screen)
        extract_all_btn.draw(screen)
        replace_btn.draw(screen)
        toggle_btn.draw(screen)
        extract_btn.update_options_position()
        extract_all_btn.update_options_position()
        replace_btn.update_options_position()

        # Texture info overlay
        if archive and selected >= 0:
            t = archive.textures[selected]
            lines = [
                f"ID:     {t.id}",
                f"Name:   {t.name or '<none>'}",
                f"Offset: 0x{t.offset:X}",
                f"Size:   {t.size} B",
                f"Res:    {t.w}×{t.h}",
                f"Type:   {t.type}",
                f"Zoom:   {ui.zoom:.2f}×",
            ]
            info_width = max(font.size(l)[0] for l in lines) + 10
            ih = len(lines) * 17 + 10
            ix = LIST_W + 8
            iy = 8
            bg = pygame.Surface((info_width, ih), pygame.SRCALPHA)
            bg.fill(C_INFO_BG)
            screen.blit(bg, (ix - 4, iy - 4))
            for j, line in enumerate(lines):
                screen.blit(font.render(line, True, C_INFO_TXT), (ix, iy + j * 17))

        # Drop hint
        if not archive:
            cx = LIST_W + prev_w // 2
            t = fontS.render("Drop a texture.pak file onto this window", True, (90, 90, 110))
            screen.blit(t, (cx - t.get_width()//2, H//2 - 12))

        # Status message
        if ui.message and now < ui.message_until:
            ms = font.render(ui.message, True, C_MSG)
            screen.blit(ms, (LIST_W + 10, H - 20))
        else:
            ht = fontS.render(
                "[← →] navigate   [F] fit   [1] reset zoom   [/] search   [RMB] reset view",
                True, C_HINT)
            screen.blit(ht, (LIST_W + 8, H - 18))

        # Modal progress dialog on top
        if active_dialog:
            active_dialog.draw(screen)

        pygame.display.flip()
        clock.tick(30)

    if archive:
        archive.close()

def draw_checker(screen, rect, tile=8):
    x0, y0, rw, rh = rect
    ca, cb = (175, 175, 175), (215, 215, 215)
    for ty in range(y0, y0 + rh, tile):
        for tx in range(x0, x0 + rw, tile):
            c = ca if ((tx // tile) + (ty // tile)) % 2 == 0 else cb
            pygame.draw.rect(screen, c, (tx, ty, min(tile, x0+rw-tx), min(tile, y0+rh-ty)))

if __name__ == "__main__":
    run()