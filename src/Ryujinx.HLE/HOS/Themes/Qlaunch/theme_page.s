; [Nextendo, HOME Menu themes] Code added to the HOME Menu (qlaunch 22.5.0): the themes installed
; on the SD card are listed in "System Settings > Themes" after Basic White and Basic Black.
; Addresses are offsets in the module (the image as loaded); the emulator puts this at 0x425F000
; and its data page at 0x4267000 (@DATA).
;
; The page (0xD78 bytes) makes its two entries from the template L_CheckTheme_00: an entry
; object of 0x20 bytes (vtable 0x10D8338; +8 its layout, +0x10 its button), a separator line
; under it (page+0xD00, +0xD08; +0xD10 the line above the first entry), three callbacks on the
; button (focus 0x98DD80, decide 0x98DE50, the check group at page+0xD18), the button chained in
; the page's focus list and added to the page's buttons (page+0xD48: count, capacity, array).
; When shown, each entry is given to the scrolling list (0xB30050) with three callbacks that
; place, move and hide it; the page's own only know entries 0 and 1. The added entries are made
; the same way, with callbacks of ours; their objects live in the data page.
;
; Data page (written by the emulator):
;   +0x000  u32   number of added entries (themes)
;   +0x004  s32   theme in use (added entry number), -1 for none
;   +0x008  u64   build argument, kept
;   +0x010  u64   page->vtable[0x3B8](page), kept
;   +0x018  u64   *(page+0xBC0)->+0x30, kept
;   +0x020  u64   entry template layout name (pointer)
;   +0x028  u64   line template layout name (pointer)
;   +0x030  s32   theme picked on the page (added entry number), -1 for Basic White/Black
;   +0x038  u64   the page while built (0 otherwise)
;   +0x040  char  entry template layout name
;   +0x080  char  line template layout name
;   +0x100  0x20 bytes per added entry: its object
;   +0x300  u64   per added entry: its line
;   +0x3D4  u32   set by the emulator: the page is to be rebuilt (main_poll)
;   +0x3D8  s32   the entry last picked (0 and 1: the page's own)
;   +0x3DC  u32   the pages of a theme are shown (the page's own two entries named by the emulator)
;   +0x3E0  u32   the added entries made (build), +0x3E4 how many to make
;   +0x3E8  s32   the entry to focus when the page is rebuilt, -1 for the page's own choice
;   +0x3EC  u32   the added entries shown (refresh); +0x000 how many to show
;   +0x2400 the text of entry 0 in the pages of a theme (UTF-16), +0x2500 that of entry 1
;   +0x2600 "Nx_FooterText_Customize", the label of the footer text of X on a theme
;   +0x2800 0x40 bytes per entry of the pages of a theme: its picture's holder
;   +0x3F0  "NxThemes", the debug string sent when the page is built
;   +0x3F8  u32   the position of "X Customise" in the footer
;   +0x400  0x100 bytes per added entry: its name (UTF-16)
;   +0x1400 0x40 bytes per added entry: its picture (the page's picture holder, vtable 0x10E1118)
;   +0x1800 0x20 bytes per added entry: its picture's path in the program's files
;
; Entry points (the table below, 4 bytes each):
;   +0x00  build       vtable slot 0x10D4020 (+0x3C0)
;   +0x04  refresh     vtable slot 0x10D4028 (+0x3C8)
;   +0x08  deactivate  vtable slot 0x10D4060 (+0x400)
;   +0x0C  destroy     vtable slot 0x10D4068 (+0x408)

    b build
    b refresh
    b deactivate
    b destroy
    b index_of_button             ; +0x10
    b index_of_focus              ; +0x14
    b get_focused                 ; +0x18
    b pick                        ; +0x1C
    b current                     ; +0x20
    b entry_button                ; +0x24
    b main_poll                   ; +0x28
    b footer_focus                ; +0x2C
    b settings_scene              ; +0x30
    b themes_scene                ; +0x34
    b uses_select_boxes           ; +0x38 (vtable slot +0x420 of the page)
    b restart_callback_copy       ; +0x3C (the restart dialog's callbacks: their vtables)
    b restart_callback_buttons    ; +0x40
    b restart_callback_dialog     ; +0x44
    b vgc_welcome                 ; +0x48 (the virtual game cards page's welcome)

; ---------------------------------------------------------------------------------------------
; [Nextendo, HOME Menu work] The settings menu builds the page of a section (a switch on its
; number, 0x7DDD4C): Themes (11) and, in the place of Bluetooth Audio (3), "Ryujinx | Nextendo",
; both the themes page. The data page (+0x3C0) tells the emulator which one is built.

settings_scene:
    adrp x9, @DATA
    mov w10, #1
    str w10, [x9, #0x3c0]
    b #0x7e3e10

themes_scene:
    adrp x9, @DATA
    str wzr, [x9, #0x3c0]
    b #0x7e3e10

; ---------------------------------------------------------------------------------------------
; helpers for the page's own code (patched to call them; see build.py)

; the number of entries (x0 = the page) -> w0
entry_count:
    adrp x8, @DATA
    ldr w0, [x8]
    cmp w0, #16
    mov w9, #16
    csel w0, w0, w9, le
    add w0, w0, #2
    ret

; the button of entry i: x0 = the page, w1 = i -> x0
entry_button:
    stp x29, x30, [sp, #-0x10]!
    mov x29, sp
    bl entry_object
    ldr x0, [x0, #0x10]
    ldp x29, x30, [sp], #0x10
    ret

; the entry of a button: x0 = the page, x1 = the button -> w0 (-1: none)
index_of_button:
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    stp x21, x22, [sp, #0x20]
    mov x19, x0
    mov x20, x1
    bl entry_count
    mov w21, w0
    mov w22, wzr
index_of_button_loop:
    cmp w22, w21
    b.ge index_of_button_none
    mov x0, x19
    mov w1, w22
    bl entry_button
    cmp x0, x20
    b.eq index_of_button_found
    add w22, w22, #1
    b index_of_button_loop
index_of_button_none:
    mov w22, #-1
index_of_button_found:
    mov w0, w22
    ldp x21, x22, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x30
    ret

; the entry of a focus element (button+0x430): x0 = the page, x1 = the element -> w0 (-1: none)
index_of_focus:
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    stp x21, x22, [sp, #0x20]
    mov x19, x0
    mov x20, x1
    bl entry_count
    mov w21, w0
    mov w22, wzr
index_of_focus_loop:
    cmp w22, w21
    b.ge index_of_focus_none
    mov x0, x19
    mov w1, w22
    bl entry_button
    cbz x0, index_of_focus_next
    ldr x0, [x0, #0x430]
    cmp x0, x20
    b.eq index_of_focus_found
index_of_focus_next:
    add w22, w22, #1
    b index_of_focus_loop
index_of_focus_none:
    mov w22, #-1
index_of_focus_found:
    mov w0, w22
    ldp x21, x22, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x30
    ret

; the page's focused button (its method +0x388): x0 = the page -> x0 (0: none)
get_focused:
    stp x29, x30, [sp, #-0x20]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    mov x19, x0
    mov x0, x19
    ldr x9, [x0, #0xb68]
    ldr x9, [x9, #0x18]
    ldr x1, [x9, #0x18]
    cbz x1, get_focused_none
    bl index_of_focus
    tbnz w0, #31, get_focused_none
    mov w1, w0
    mov x0, x19
    bl entry_button
    b get_focused_done
get_focused_none:
    mov x0, xzr
get_focused_done:
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x20
    ret

; an entry is picked (index in w20, the caller's): the theme kept for the emulator, and the
; colour set to give the system -> w0 (a theme is drawn on Basic Black)
; (it replaces one instruction: only x0, x16 and x17 may change)
pick:
    adrp x16, @DATA
    str w20, [x16, #0x3d8]
    ldr w17, [x16, #0x3dc]
    cbz w17, pick_entry
    adrp x17, #0x425a000          ; the pages of a theme: the colour set in use stays
    ldr x17, [x17, #0xd80]
    ldr w0, [x17, #8]
    ret
pick_entry:
    subs w17, w20, #2
    b.ge pick_theme
    mov w17, #-1
pick_theme:
    str w17, [x16, #0x30]
    mov w0, #1
    cmp w20, #1
    b.gt pick_done
    mov w0, w20
pick_done:
    ret

; the entry to check when the page is shown: the theme in use, else the colour set (w9 the
; caller's colour state, at x9+8) -> w0
current:
    adrp x8, @DATA
    ldr w10, [x8, #0x3dc]
    cbz w10, current_theme
    ldr w0, [x8, #0x3e0]          ; the pages of a theme: the last entry made, never shown
    add w0, w0, #1
    ret
current_theme:
    ldr w10, [x8, #4]
    tbnz w10, #31, current_colour
    add w0, w10, #2
    ret
current_colour:
    ldr w0, [x9, #8]
    cmp w0, #1
    mov w10, #1
    csel w0, w0, w10, ls
    ret

; ---------------------------------------------------------------------------------------------
; the page is built: the original, then the added entries and their lines
; x0 = the page, x1 = the build argument
build:
    stp x29, x30, [sp, #-0x100]!
    mov x29, sp
    stp x19, x20, [sp, #0xb0]
    stp x21, x22, [sp, #0xc0]
    stp x23, x24, [sp, #0xd0]
    stp x25, x26, [sp, #0xe0]
    stp x27, x28, [sp, #0xf0]
    mov x19, x0
    mov x20, x1
    ; the emulator is told (debug string "NxThemes"): it lists the themes as they are now
    adrp x0, @DATA
    add x0, x0, #0x3f0
    mov x1, #8
    svc #0x27
    mov x0, x19
    mov x1, x20
    bl #0x98ceb0                  ; Basic White, Basic Black

    adrp x23, @DATA
    str x19, [x23, #0x38]         ; the page, for the main loop hook
    ldr w8, [x23, #0x3e4]
    cbz w8, build_done
    str x20, [x23, #8]

    ; the entry template's layout name
    adrp x1, #0xc10000
    add x1, x1, #0xe0c            ; "L_CheckTheme_00"
    add x2, x23, #0x40
    mov x0, x19
    bl part_layout_name
    add x8, x23, #0x40
    str x8, [x23, #0x20]
    ; the line template's
    adrp x1, #0xc05000
    add x1, x1, #0x159            ; "L_Line_00"
    add x2, x23, #0x80
    mov x0, x19
    bl part_layout_name
    add x8, x23, #0x80
    str x8, [x23, #0x28]

    ldr x8, [x19]
    mov x0, x19
    ldr x8, [x8, #0x3b8]
    blr x8
    str x0, [x23, #0x10]
    ldr x8, [x19, #0xbc0]
    ldr x8, [x8, #0x30]
    str x8, [x23, #0x18]

    mov w24, wzr
build_entry:
    adrp x23, @DATA
    ldr w8, [x23, #0x3e4]
    cmp w24, w8
    b.ge build_done
    cmp w24, #16
    b.ge build_done
    mov x0, x19
    mov w1, w24
    bl add_entry
    mov x0, x19
    mov w1, w24
    bl add_line
    mov x0, x19
    mov w1, w24
    bl add_picture
    add w24, w24, #1
    b build_entry

build_done:
    ; the "Ryujinx | Nextendo" section: a select box per setting, as System has
    adrp x8, @DATA
    ldr w9, [x8, #0x3c0]
    cbz w9, build_done_boxes
    mov x0, x19
    bl settings_build
build_done_boxes:
    adrp x8, @DATA                ; the entries made
    ldr w9, [x8, #0x3e4]
    cmp w9, #16
    mov w10, #16
    csel w9, w9, w10, le
    str w9, [x8, #0x3e0]
    ldp x27, x28, [sp, #0xf0]
    ldp x25, x26, [sp, #0xe0]
    ldp x23, x24, [sp, #0xd0]
    ldp x21, x22, [sp, #0xc0]
    ldp x19, x20, [sp, #0xb0]
    ldp x29, x30, [sp], #0x100
    ret

; "%s.bflyt" of the layout of a part of the page's layout
; x0 = the page, x1 = the part's pane name, x2 = where to write it (0x40 bytes)
part_layout_name:
    stp x29, x30, [sp, #-0x80]!
    mov x29, sp
    stp x19, x20, [sp, #0x70]
    mov x20, x2
    ; the formatter's buffer: { char* text, s32 room, ... } with the text after it
    add x8, sp, #0x10
    strb wzr, [sp, #0x5b]
    add x8, x8, #0xc
    strb wzr, [sp, #0x1c]
    str x8, [sp, #0x10]
    mov w8, #0x40
    str w8, [sp, #0x18]
    ldr x8, [x0, #0xb50]
    ldr x0, [x8, #0xa0]
    bl #0x1b5880                  ; the part
    ldr x8, [x0, #0xe8]
    adrp x1, #0xbff000
    add x1, x1, #0x393            ; "%s.bflyt"
    add x0, sp, #0x10
    ldr x2, [x8, #0x30]
    bl #0x441170
    ldr x8, [sp, #0x10]
    ldrsw x9, [sp, #0x18]
    add x8, x8, x9
    sturb wzr, [x8, #-1]
    ldr x1, [sp, #0x10]
    mov x0, x20
    mov x2, #0x40
    bl copy_bytes
    ldp x19, x20, [sp, #0x70]
    ldp x29, x30, [sp], #0x80
    ret

; x0 = destination, x1 = source, x2 = length
copy_bytes:
    cbz x2, copy_done
    ldrb w8, [x1], #1
    strb w8, [x0], #1
    sub x2, x2, #1
    b copy_bytes
copy_done:
    ret

; the object of entry i (x0 = the page, w1 = i): page+0xCC0 for 0 and 1, the data page after
entry_object:
    cmp w1, #2
    b.ge entry_object_added
    add x0, x0, #0xcc0
    add x0, x0, x1, lsl #5
    ret
entry_object_added:
    adrp x0, @DATA
    add x0, x0, #0x100
    sub w1, w1, #2
    add x0, x0, x1, lsl #5
    ret

; the line under entry i (x0 = the page, w1 = i)
entry_line:
    cmp w1, #2
    b.ge entry_line_added
    add x0, x0, #0xd00
    ldr x0, [x0, x1, lsl #3]
    ret
entry_line_added:
    adrp x0, @DATA
    add x0, x0, #0x300
    sub w1, w1, #2
    ldr x0, [x0, x1, lsl #3]
    ret

; ---------------------------------------------------------------------------------------------
; one added entry (as the page makes Basic Black)
; x0 = the page, w1 = added entry number (its index in the page is 2 + it)
add_entry:
    stp x29, x30, [sp, #-0xa0]!
    mov x29, sp
    stp x19, x20, [sp, #0x50]
    stp x21, x22, [sp, #0x60]
    stp x23, x24, [sp, #0x70]
    stp x25, x26, [sp, #0x80]
    stp x27, x28, [sp, #0x90]
    ; the original's temporaries (at its sp, 0x30 bytes) are at x29 here
    add x29, sp, #0x10
    mov x19, x0
    str w1, [sp, #0x40]

    ; the entry's object: vtable 0x10D8338, the rest zero
    adrp x23, @DATA
    add x24, x23, #0x100
    add x24, x24, x1, lsl #5
    adrp x8, #0x10d8000
    add x8, x8, #0x338
    stp x8, xzr, [x24]
    stp xzr, xzr, [x24, #0x10]

    ; set up from the template
    ldr x27, [x23, #0x20]
    ldr x21, [x23, #8]
    ldr x28, [x23, #0x18]
    ldr x22, [x23, #0x10]
    add x20, x29, #0x10
    ldr x8, [x24]
    mov x0, x24
    mov x1, x29
    stp x27, x21, [x29]
    ldr x8, [x8, #0x28]
    stp xzr, x28, [x29, #0x10]
    str x22, [x29, #0x20]
    blr x8

    ; its layout under N_Root
    ldr x8, [x19, #0xb50]
    adrp x1, #0xc05000
    add x1, x1, #0x369            ; "N_Root"
    ldr x22, [x24, #0x10]
    mov w2, #1
    ldr x8, [x8, #0xa0]
    ldr x0, [x8, #0x18]
    ldr x8, [x0]
    ldr x8, [x8, #0x78]
    blr x8
    ldr x8, [x24, #8]
    ldr x8, [x8, #0xa0]
    ldr x1, [x8, #0x18]
    ldr x8, [x1, #0x18]
    cbz x8, e_append
    mov x21, x0
    mov x0, x8
    bl #0x1c8280
    ldr x8, [x24, #8]
    mov x0, x21
    ldr x8, [x8, #0xa0]
    ldr x1, [x8, #0x18]
e_append:
    bl #0x1c81d0

    ldr x21, [x24, #0x10]
    ldr x0, [x21, #0x6b8]
    cbz x0, e_hidden
    mov w1, wzr
    bl #0xb8d060
    tbz w0, #0, e_focus
    mov x0, x21
    mov w1, wzr
    bl #0xafde10
    b e_focus
e_hidden:
    ldr x8, [x21, #0x20]
    ldr x8, [x8, #0x18]
    ldrb w9, [x8, #0x58]
    and w9, w9, #0xfe
    strb w9, [x8, #0x58]

    ; focus callback (0x98DD80, on the page)
e_focus:
    adrp x26, #0x1086000
    add x26, x26, #0x78
    ldr x8, [x24, #0x10]
    stp x26, x19, [x29, #0x10]
    ldr x21, [x8, #0x430]
    adrp x8, #0x98d000
    add x8, x8, #0xd80
    stp x8, xzr, [x29, #0x20]
    adrp x8, #0x108a000
    add x8, x8, #0x988
    stp x8, x20, [x29]
    ldr x0, [x21, #0x38]
    cbz x0, e_focus_new
    ldr x8, [x0]
    ldr x8, [x8, #8]
    blr x8
    ldr x0, [x29, #8]
    cbz x0, e_focus_none
    ldr x26, [x0]
    b e_focus_clone
e_focus_new:
    mov x0, x20
e_focus_clone:
    ldr x8, [x26, #0x18]
    add x1, x21, #0x40
    blr x8
    mov x8, x0
    ldr x0, [x29, #8]
    str x8, [x21, #0x38]
    cbz x0, e_decide
    ldr x8, [x0]
    ldr x8, [x8, #8]
    blr x8
    b e_decide
e_focus_none:
    str xzr, [x21, #0x38]

    ; decide callback (0x98DE50, on the page)
e_decide:
    ldr x22, [x24, #0x10]
    ldr x0, [x22, #0x260]
    adrp x8, #0x1083000
    add x8, x8, #0x5d8
    stp x8, x19, [x29, #0x10]
    adrp x8, #0x98d000
    add x8, x8, #0xe50
    stp x8, xzr, [x29, #0x20]
    adrp x8, #0x108a000
    add x8, x8, #0x988
    adrp x21, #0x108a000
    add x21, x21, #0x988
    stp x8, x20, [x29]
    cbz x0, e_decide_clone
    ldr x8, [x0]
    ldr x8, [x8, #8]
    blr x8
    ldr x20, [x29, #8]
    cbz x20, e_decide_none
e_decide_clone:
    ldr x8, [x20]
    add x1, x22, #0x268
    mov x0, x20
    ldr x8, [x8, #0x18]
    blr x8
    mov x8, x0
    ldr x0, [x29, #8]
    str x8, [x22, #0x260]
    cbz x0, e_chain
    ldr x8, [x0]
    ldr x8, [x8, #8]
    blr x8
    b e_chain
e_decide_none:
    str xzr, [x22, #0x260]

    ; the button in the page's focus list
e_chain:
    add x20, x29, #0x10
    ldr x8, [x19]
    mov x0, x19
    ldr x8, [x8, #0x368]
    blr x8
    ldr x9, [x22, #0x430]
    cbz x9, e_flags
    mov x8, x9
    ldr x10, [x8, #8]!
    cbnz x10, e_flags
    ldr x10, [x0, #0x18]
    ldr x11, [x10, #0x58]!
    cbz x11, e_chain_first
e_chain_last:
    mov x10, x11
    ldr x11, [x11, #0x10]
    cbnz x11, e_chain_last
    str x8, [x10, #0x10]
    str x10, [x9, #0x10]
    ldr x10, [x10]
    b e_chain_end
e_chain_first:
    str x8, [x10]
e_chain_end:
    str x10, [x8]
e_flags:
    ldrb w8, [x22, #0x6c4]
    cmp w8, #2
    b.ne e_group
    ldrh w9, [x22, #0x6c0]
    and w10, w9, #0xfffffff7
    ldrb w8, [x19, #0xd58]
    orr w9, w9, #0xc
    cmp w8, #0
    orr w10, w10, #4
    csel w8, w9, w10, ne
    strh w8, [x22, #0x6c0]

    ; the check group (page+0xD18)
e_group:
    add x0, x29, #0x10
    adrp x8, #0x10e1000
    add x8, x8, #0x698
    add x25, x19, #0xd18
    stp x8, x25, [x29, #0x10]
    ldr x8, [x22, #0x630]
    stp x21, x0, [x29]
    cbz x8, e_group_clone
    ldr x9, [x8]
    mov x0, x8
    ldr x9, [x9, #8]
    blr x9
    ldr x0, [x29, #8]
    cbz x0, e_group_none
e_group_clone:
    ldr x8, [x0]
    add x1, x22, #0x638
    ldr x8, [x8, #0x18]
    blr x8
    mov x8, x0
    ldr x0, [x29, #8]
    str x8, [x22, #0x630]
    cbz x0, e_list
    ldr x8, [x0]
    ldr x8, [x8, #8]
    blr x8
    b e_list
e_group_none:
    str xzr, [x22, #0x630]

    ; the page's buttons
e_list:
    ldrsw x8, [x19, #0xd48]
    ldr w9, [x19, #0xd4c]
    cmp w8, w9
    b.ge e_name
    ldr x9, [x19, #0xd50]
    str x22, [x9, x8, lsl #3]
    ldr w8, [x19, #0xd48]
    add w8, w8, #1
    str w8, [x19, #0xd48]

    ; its name: CheckTheme_<index>
e_name:
    add x23, x29, #0xc
    mov w8, #0x20
    adrp x1, #0xbde000
    add x1, x1, #0x73a            ; "CheckTheme_%02d"
    mov x0, x29
    str x23, [x29]
    str w8, [x29, #8]
    ldr w2, [sp, #0x40]
    add w2, w2, #2
    strb wzr, [x29, #0x2b]
    strb wzr, [x29, #0xc]
    bl #0x441170
    ldr x8, [x29]
    ldrsw x9, [x29, #8]
    add x8, x8, x9
    sturb wzr, [x8, #-1]
    ldr x8, [x24, #8]
    ldr x1, [x29]
    ldr x8, [x8, #0xa0]
    ldr x0, [x8, #0x18]
    bl #0x1c7690

    ldp x27, x28, [sp, #0x90]
    ldp x25, x26, [sp, #0x80]
    ldp x23, x24, [sp, #0x70]
    ldp x21, x22, [sp, #0x60]
    ldp x19, x20, [sp, #0x50]
    ldp x29, x30, [sp], #0xa0
    ret

; ---------------------------------------------------------------------------------------------
; the line under an added entry (as the page makes its lines)
; x0 = the page, w1 = added entry number
add_line:
    stp x29, x30, [sp, #-0xc0]!
    mov x29, sp
    stp x19, x20, [sp, #0x90]
    stp x21, x22, [sp, #0xa0]
    stp x23, x24, [sp, #0xb0]
    mov x19, x0
    str w1, [sp, #0x88]

    ldr x21, [x19, #0x50]
    mov w1, #0x120
    mov w2, #8
    ldr x0, [x21, #0x28]
    ldr x8, [x0]
    ldr x8, [x8, #0x10]
    blr x8                        ; the layout object (0x120 bytes)
    mov x20, x0
    mov w1, #5
    str xzr, [x20, #0x20]
    mov x8, x20
    mov x9, x20
    add x2, x20, #0x70
    str xzr, [x8, #0x68]!
    str x8, [x9, #0x60]!
    mov x8, x20
    stp xzr, xzr, [x20]
    str x9, [x8, #0x58]!
    mov x9, x20
    stp xzr, xzr, [x20, #0x10]
    str x8, [x9, #0x50]!
    mov x8, x20
    str xzr, [x0, #0x28]!
    str x9, [x8, #0x48]!
    stp xzr, x8, [x20, #0x30]
    str x8, [x20, #0x40]
    bl #0x33c930
    adrp x23, #0x10e9000
    add x23, x23, #0x148
    stur xzr, [x20, #0xb6]
    add x8, x20, #0xc8
    stp xzr, xzr, [x20, #0xa8]
    stp xzr, xzr, [x20, #0x98]
    stp x23, x8, [x20, #0xc0]
    stp x8, xzr, [x20, #0xd0]
    stp xzr, xzr, [x20, #0x100]
    stp xzr, xzr, [x20, #0x110]
    ldp x8, x9, [x21, #0x28]
    stp x8, x9, [x20, #0x18]

    ; kept for the entry
    adrp x23, @DATA
    ldr w8, [sp, #0x88]
    add x9, x23, #0x300
    str x20, [x9, x8, lsl #3]

    ; built from the line template
    add x22, sp, #0x10
    adrp x24, #0x4251000
    ldr x8, [x24, #0x4d8]
    ldr x8, [x8]
    ldr x9, [x23, #0x28]
    mov w10, #0x640
    stp x8, x9, [x22]
    str x10, [x22, #0x10]
    strh wzr, [x22, #0x18]
    strb wzr, [x22, #0x1a]
    stp xzr, xzr, [x22, #0x20]
    strb wzr, [x22, #0x30]
    stp xzr, xzr, [x22, #0x38]
    mov x0, x20
    mov x1, x22
    bl #0xb93cb0
    ldr x8, [x20, #0xa0]
    ldr x8, [x8, #0x18]
    ldrb w9, [x8, #0x58]
    and w9, w9, #0xfe
    strb w9, [x8, #0x58]

    ldp x23, x24, [sp, #0xb0]
    ldp x21, x22, [sp, #0xa0]
    ldp x19, x20, [sp, #0x90]
    ldp x29, x30, [sp], #0xc0
    ret

; ---------------------------------------------------------------------------------------------
; the picture of an added entry (as the page loads theme/ThemeIcon_00.jpg)
; x0 = the page, w1 = added entry number
add_picture:
    adrp x2, @DATA
    add x2, x2, #0x1000
    add x2, x2, #0x400
    add x2, x2, x1, lsl #6
; the same, in the holder x2 (the pages of a theme have holders of their own: a picture freed
; and made again in the same place is drawn as the one before)
add_picture_at:
    sub sp, sp, #0xe0
    stp x29, x30, [sp, #0xd0]
    add x29, sp, #0xd0
    stp x19, x20, [sp, #0xa0]
    stp x21, x22, [sp, #0xb0]
    stp x23, x24, [sp, #0xc0]
    mov x19, x0
    mov w23, w1
    mov x20, x2

    ; none when the theme has no picture
    adrp x24, @DATA
    add x8, x24, #0x1000
    add x8, x8, #0x800
    add x8, x8, x23, lsl #5
    ldrb w8, [x8]
    cbz w8, add_picture_done
    ; the holder: vtable, the rest zero
    adrp x8, #0x10e1000
    add x8, x8, #0x118
    stp x8, xzr, [x20]
    stp xzr, xzr, [x20, #0x10]
    stp xzr, xzr, [x20, #0x20]
    stp xzr, xzr, [x20, #0x30]

    ; its texture (92x54), as the page asks for it
    mov w8, #0x1d
    adrp x9, #0xef9000
    add x9, x9, #0x150
    stp x8, x9, [sp, #0x70]
    ldr x8, [x19, #0x50]
    adrp x11, #0x10e8000
    add x11, x11, #0x180
    strh wzr, [sp, #0x20]
    ldp x9, x10, [x8, #0x28]
    ldr x8, [x8, #0x28]
    stp x11, x9, [sp, #0x50]
    mov x9, #0x5c
    mov w11, #0x20
    movk x9, #0x36, lsl #32
    str w11, [sp, #0x24]
    stp x10, x9, [sp, #0x60]
    stp x8, x9, [sp, #0x10]
    mov x0, x20
    add x1, sp, #0x50
    bl #0xafb520

    ; the picture, from the program's files
    adrp x8, #0x1143000
    ldr x8, [x8, #0xa40]
    ldr x21, [x20, #0x30]
    ldr x0, [x21, #0x18]
    blr x8
    mov x22, x0
    mov x0, x21
    bl #0x3cf320
    ldr x9, [x20, #0x30]
    add x8, x22, x0
    add x1, x24, #0x1000
    add x1, x1, #0x800
    add x1, x1, x23, lsl #5
    ldr x9, [x9, #0x10]
    stp x8, x9, [sp]
    mov x0, sp
    bl #0xbc0a50
    mov x8, #2
    mov x9, #4
    movk x8, #3, lsl #32
    movk x9, #5, lsl #32
    stp x8, x9, [sp, #0x30]
    add x1, sp, #0x30
    mov x0, x20
    ldr x8, [x20]
    ldr x8, [x8, #0x28]
    blr x8

    ; shown in the entry's P_Icon
    mov x0, x19
    add w1, w23, #2
    bl entry_button
    cbz x0, add_picture_done
    ldr x8, [x0, #0x20]
    ldr x0, [x8, #0x18]
    adrp x1, #0xbe4000
    add x1, x1, #0x8cc            ; "P_Icon"
    bl #0xb94e20
    cbz x0, add_picture_done
    bl #0x1c99d0
    ldr x8, [x20, #0x38]
    cbz x8, add_picture_done
    ldr x9, [x0, #0x18]
    str x8, [x9, #8]
add_picture_done:
    ldp x23, x24, [sp, #0xc0]
    ldp x21, x22, [sp, #0xb0]
    ldp x19, x20, [sp, #0xa0]
    ldp x29, x30, [sp, #0xd0]
    add sp, sp, #0xe0
    ret

; the picture of holder x2 shown in entry w1 (added entry number) of page x0
show_holder:
    stp x29, x30, [sp, #-0x20]!
    mov x29, sp
    str x19, [sp, #0x10]
    mov x19, x2
    ldr x8, [x19]
    cbz x8, show_holder_done
    add w1, w1, #2
    bl entry_button
    cbz x0, show_holder_done
    ldr x8, [x0, #0x20]
    ldr x0, [x8, #0x18]
    adrp x1, #0xbe4000
    add x1, x1, #0x8cc            ; "P_Icon"
    bl #0xb94e20
    cbz x0, show_holder_done
    bl #0x1c99d0
    ldr x8, [x19, #0x38]
    cbz x8, show_holder_done
    ldr x9, [x0, #0x18]
    str x8, [x9, #8]
show_holder_done:
    ldr x19, [sp, #0x10]
    ldp x29, x30, [sp], #0x20
    ret

; the picture of an added entry released (as the page releases its own)
; x0 = the holder
release_picture:
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    str x21, [sp, #0x20]
    mov x19, x0
    ldr x8, [x19]
    cbz x8, release_picture_done
    bl #0xafb2a0
    add x20, x19, #0x20
    ldr x0, [x20, #0x10]
    adrp x8, #0x10e1000
    add x8, x8, #0x98
    stur x8, [x20, #-0x20]
    cbz x0, release_picture_buffer
    ldr x8, [x0]
    ldr x8, [x8, #8]
    blr x8
release_picture_buffer:
    ldr x21, [x20, #8]
    cbz x21, release_picture_image
    add x0, x21, #0x68
    bl #0x3d1a00
    mov x0, x21
    bl #0x3d1d60
    mov x0, x21
    mov w1, #0x130
    bl #0x33c560
release_picture_image:
    ldr x21, [x20]
    cbz x21, release_picture_clear
    add x0, x21, #0x58
    bl #0x3d1a00
    mov x0, x21
    mov w1, #0xe8
    bl #0x33c560
release_picture_clear:
    stp xzr, xzr, [x19]
    stp xzr, xzr, [x19, #0x10]
    stp xzr, xzr, [x19, #0x20]
    stp xzr, xzr, [x19, #0x30]
release_picture_done:
    ldr x21, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x30
    ret

; ---------------------------------------------------------------------------------------------
; an entry unchecked (as the page checks one: button+0x6C0 bit 0, then its functor at +0x690
; given {button, 0, checked}): x0 = its button
uncheck:
    cbz x0, uncheck_done
    ldrh w8, [x0, #0x6c0]
    stp x29, x30, [sp, #-0x20]!
    mov x29, sp
    and w8, w8, #0xfffe
    strh w8, [x0, #0x6c0]
    mov x1, x0
    ldr x0, [x0, #0x690]
    cbz x0, uncheck_end
    str x1, [sp, #0x10]
    str wzr, [sp, #0x18]
    strb wzr, [sp, #0x1c]
    add x1, sp, #0x10
    ldr x8, [x0]
    ldr x8, [x8, #0x20]
    blr x8
uncheck_end:
    ldp x29, x30, [sp], #0x20
uncheck_done:
    ret

; ---------------------------------------------------------------------------------------------
; the picture of an entry shown or not: x0 = its button, w1 = shown
show_picture:
    adrp x2, #0xbe4000
    add x2, x2, #0x8cc            ; "P_Icon"
    b show_pane
; its check shown or not
show_check:
    adrp x2, #0xc12000
    add x2, x2, #0xe89            ; "N_Check"
; a pane of an entry shown or not: x0 = its button, w1 = shown, x2 = the pane's name
show_pane:
    cbz x0, show_picture_done
    stp x29, x30, [sp, #-0x20]!
    mov x29, sp
    str x19, [sp, #0x10]
    mov w19, w1
    ldr x8, [x0, #0x20]
    ldr x0, [x8, #0x18]
    mov x1, x2
    bl #0xb94e20
    cbz x0, show_picture_end
    ldrb w9, [x0, #0x58]
    and w9, w9, #0xfe
    orr w9, w9, w19
    strb w9, [x0, #0x58]
show_picture_end:
    ldr x19, [sp, #0x10]
    ldp x29, x30, [sp], #0x20
show_picture_done:
    ret

; ---------------------------------------------------------------------------------------------
; the focus is on entry w2 of page x19: on a theme, "X Customise" in the footer (the page's
; footer at page+0xB38, its data at +8: a byte set when changed, then entries of 0x20 bytes:
; u32 position, u32 key (A 1, B 2, X 8), the text's label, ..., u16 shown); elsewhere not.
; Only w2 and x19 are kept.
footer_focus:
    stp x29, x30, [sp, #-0x20]!
    mov x29, sp
    str w2, [sp, #0x10]
    ldr x8, [x19, #0xb38]
    cbz x8, footer_focus_done
    ldr x8, [x8, #8]
    cbz x8, footer_focus_done
    add x10, x8, #0x48            ; the third entry
    adrp x9, @DATA
    ldr w11, [x9, #0x3dc]
    cbnz w11, footer_focus_off
    subs w11, w2, #2
    b.lt footer_focus_off
    ldr w12, [x9]
    cmp w11, w12
    b.ge footer_focus_off
    adrp x9, @DATA
    add x9, x9, #0x2000
    add x9, x9, #0x600            ; its text: "Nx_FooterText_Customize"
    adrp x12, @DATA
    ldr w11, [x12, #0x3f8]        ; its position
    mov w12, #8
    stp w11, w12, [x10]
    ldr x11, [x8, #0x18]
    stp x9, x11, [x10, #8]
    mov w11, #1
    strh w11, [x10, #0x18]
    strb wzr, [x10, #0x1a]
    b footer_focus_changed
footer_focus_off:
    ldp x11, x12, [x8, #0x68]     ; as the fourth (not used)
    stp x11, x12, [x10]
    ldp x11, x12, [x8, #0x78]
    stp x11, x12, [x10, #0x10]
footer_focus_changed:
    mov w11, #1
    strb w11, [x8]
    ; shown now (as the page does when it gets the focus: 0xB26A20)
    mov x1, x8
    adrp x8, #0x4254000
    ldr x0, [x8, #0x370]
    mov w2, wzr
    cbz x0, footer_focus_done
    bl #0xb26a20
footer_focus_done:
    ldr w2, [sp, #0x10]
    ldp x29, x30, [sp], #0x20
    ret

; ---------------------------------------------------------------------------------------------
; the text of an entry: x0 = its button, x1 = the text (UTF-16)
set_text:
    cbz x0, set_text_done
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    str x19, [sp, #0x20]
    mov x19, x1
    ldr x8, [x0, #0x20]
    ldr x0, [x8, #0x18]
    adrp x1, #0xbfe000
    add x1, x1, #0xc8a            ; "T_00"
    bl #0xb94e20
    cbz x0, set_text_end
    str x0, [sp, #0x10]
    str x19, [sp, #0x18]
    add x0, sp, #0x10
    add x1, sp, #0x18
    bl #0xb98e80
set_text_end:
    ldr x19, [sp, #0x20]
    ldp x29, x30, [sp], #0x30
set_text_done:
    ret

; ---------------------------------------------------------------------------------------------
; the main loop, once a frame after the keys are handled (it replaces "ldr x8, [x19]" at
; 0xBB90A4): the page rebuilt when the emulator asks (+0x3D4), its focus given back.
main_poll:
    stp x29, x30, [sp, #-0x50]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    stp x21, x22, [sp, #0x20]
    bl settings_poll
    adrp x20, @DATA
    ldr w8, [x20, #0x3d4]
    cbz w8, main_poll_done
    str wzr, [x20, #0x3d4]
    ldr x19, [x20, #0x38]
    cbz x19, main_poll_done
    ; the scrolling list's place kept (+0x30, +0x38, +0x98 the top, +0x40 the bottom, +0xA0)
    ldr x8, [x19, #0xbc0]
    ldr x22, [x8, #0x28]
    cbz x22, main_poll_hide
    ldr w8, [x22, #0x160]
    str w8, [sp, #0x30]
    ldr w8, [x22, #0x30]
    str w8, [sp, #0x34]
    ldr w8, [x22, #0x40]
    str w8, [sp, #0x38]
    ldr w8, [x22, #0xa0]
    str w8, [sp, #0x3c]
main_poll_hide:
    ; the page left and shown again: its entries as the emulator wrote them (none is destroyed:
    ; what was drawn with them may still be in use)
    ldr x8, [x19]
    ldr x8, [x8, #0x400]
    mov x0, x19
    blr x8                        ; deactivate
    cbz x22, main_poll_show
    mov x0, x22
    bl #0xb30cb0                  ; the scrolling list emptied
main_poll_show:
    ldr x8, [x19]
    ldr x8, [x8, #0x3c8]
    mov x0, x19
    blr x8                        ; refresh
    ; the list back where it was, when it has as many entries or more
    cbz x22, main_poll_focus
    ldr w8, [x22, #0x160]
    ldr w9, [sp, #0x30]
    cmp w8, w9
    b.lt main_poll_focus
    ldr w8, [sp, #0x34]
    str w8, [x22, #0x30]
    str w8, [x22, #0x38]
    str w8, [x22, #0x98]
    ldr w8, [sp, #0x38]
    str w8, [x22, #0x40]
    ldr w8, [sp, #0x3c]
    str w8, [x22, #0xa0]
    mov x0, x22
    movi d0, #0
    movi d1, #0
    bl #0xb30460
main_poll_focus:
    ; the focus on the entry the emulator asks for (as the page puts it on its first: 0xB05530)
    ldr w1, [x20, #0x3e8]
    tbnz w1, #31, main_poll_done
    mov w8, #-1
    str w8, [x20, #0x3e8]
    mov x0, x19
    bl entry_button
    cbz x0, main_poll_done
    ldr x21, [x0, #0x430]
    ldr x8, [x19]
    ldr x8, [x8, #0x368]
    mov x0, x19
    blr x8
    ldr x0, [x0, #0x18]
    mov x1, x21
    mov w2, wzr
    mov w3, wzr
    bl #0xb05530
main_poll_done:
    ldp x21, x22, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x50
    ldr x8, [x19]
    ret



; ---------------------------------------------------------------------------------------------
; the page is shown: the original, then the added entries
; x0 = the page
refresh:
    stp x29, x30, [sp, #-0x140]!
    mov x29, sp
    stp x19, x20, [sp, #0x100]
    stp x21, x22, [sp, #0x110]
    stp x23, x24, [sp, #0x120]
    stp x25, x26, [sp, #0x130]
    mov x19, x0
    bl #0x98e2b0

    adrp x23, @DATA
    ldr w8, [x23, #0x3dc]
    cbz w8, refresh_own
    ldr x0, [x19, #0xcd0]
    add x1, x23, #0x2000
    add x1, x1, #0x400
    bl set_text
    ldr x0, [x19, #0xcf0]
    add x1, x23, #0x2000
    add x1, x1, #0x500
    bl set_text
refresh_own:
    ldr w8, [x23]
    cbz w8, refresh_done

    mov w24, wzr
refresh_entry:
    adrp x23, @DATA
    ldr w8, [x23]
    cmp w24, w8
    b.ge refresh_list
    cmp w24, #16
    b.ge refresh_list

    add x25, x23, #0x100
    add x25, x25, x24, lsl #5     ; the entry
    ; its layout is updated and drawn
    ldr x0, [x25, #8]
    bl #0xb94320
    ; its button in the buttons of the screen, active
    ldr x0, [x25, #0x10]
    adrp x20, #0x4254000
    mov x8, x0
    ldr x9, [x8, #8]!
    cbnz x9, r_button_on
    ldr x9, [x20, #0x400]
    ldr x10, [x9]
    cbz x10, r_button_first
r_button_last:
    mov x9, x10
    ldr x10, [x10, #0x10]
    cbnz x10, r_button_last
    str x8, [x9, #0x10]
    str x9, [x0, #0x10]
    ldr x9, [x9]
    b r_button_end
r_button_first:
    str x8, [x9]
r_button_end:
    str x9, [x8]
r_button_on:
    ldr x8, [x0]
    ldr x8, [x8, #0x28]
    blr x8
    ; its text: the theme's name
    ldr x8, [x25, #0x10]
    ldr x8, [x8, #0x20]
    ldr x0, [x8, #0x18]
    adrp x1, #0xbfe000
    add x1, x1, #0xc8a            ; "T_00"
    bl #0xb94e20
    cbz x0, r_line
    str x0, [sp, #0x10]
    add x8, x23, #0x400
    add x8, x8, x24, lsl #8
    str x8, [sp, #0x18]
    add x0, sp, #0x10
    add x1, sp, #0x18
    bl #0xb98e80
    ; its line, updated and drawn, under N_Root
r_line:
    add x8, x23, #0x300
    ldr x20, [x8, x24, lsl #3]
    mov x0, x20
    bl #0xb94320
    ldr x21, [x19, #0xb50]
    adrp x1, #0xc05000
    add x1, x1, #0x369            ; "N_Root"
    mov w2, #1
    ldr x8, [x21, #0xa0]
    ldr x0, [x8, #0x18]
    ldr x8, [x0]
    ldr x8, [x8, #0x78]
    blr x8
    mov x2, x0
    mov x0, x21
    mov x1, x20
    bl #0xb94af0

    add w24, w24, #1
    b refresh_entry

    ; the added entries in the scrolling list, after the page's two
refresh_list:
    ldr x8, [x19, #0xbc0]
    ldr x20, [x8, #0x28]
    cbz x20, refresh_done
    ldr x8, [x19, #0xb50]
    adrp x1, #0xc10000
    add x1, x1, #0xe0c            ; "L_CheckTheme_00"
    ldr x8, [x8, #0xa0]
    ldr x0, [x8, #0x18]
    bl #0xb94e20
    mov x21, x0
    ldr x8, [x19, #0xb50]
    adrp x1, #0xc05000
    add x1, x1, #0x159            ; "L_Line_00"
    ldr x8, [x8, #0xa0]
    ldr x0, [x8, #0x18]
    bl #0xb94e20
    mov x22, x0
    cbz x21, refresh_done
    cbz x22, refresh_done

    mov w24, wzr
refresh_list_entry:
    adrp x23, @DATA
    ldr w8, [x23]
    cmp w24, w8
    b.ge refresh_done
    cmp w24, #16
    b.ge refresh_done

    ; { index, width, height } then three callbacks { wrapper, inner { vtable, page, function, 0 } }
    add x11, sp, #0x40
    add w8, w24, #2
    str w8, [x11]
    ldp s2, s1, [x21, #0x50]
    ldr s0, [x22, #0x54]
    fadd s0, s0, s1
    stp s2, s0, [x11, #4]
    adrp x10, #0x108a000
    add x10, x10, #0x988
    adrp x12, #0x1085000
    add x12, x12, #0x288
    ; place (ours)
    add x9, x11, #0x20
    stp x10, x9, [x11, #0x10]
    adr x8, place_entry
    stp x12, x19, [x11, #0x20]
    stp x8, xzr, [x11, #0x30]
    ; move (ours)
    add x9, x11, #0x50
    stp x10, x9, [x11, #0x40]
    adr x8, move_entry
    stp x12, x19, [x11, #0x50]
    stp x8, xzr, [x11, #0x60]
    ; hide (ours)
    adrp x13, #0x1086000
    add x13, x13, #0x78
    add x9, x11, #0x80
    stp x10, x9, [x11, #0x70]
    adr x8, hide_entry
    stp x13, x19, [x11, #0x80]
    stp x8, xzr, [x11, #0x90]
    mov x0, x20
    mov x1, x11
    mov x2, xzr
    bl #0xb30050

    add w24, w24, #1
    b refresh_list_entry

refresh_done:
    ; the scrolling list placed again, the added entries with the page's (it places its own
    ; before they are added)
    ldr x8, [x19, #0xbc0]
    ldr x0, [x8, #0x28]
    cbz x0, refresh_placed
    movi d0, #0
    movi d1, #0
    bl #0xb30460
refresh_placed:
    ; the entries shown (their pictures: not in the pages of a theme)
    adrp x23, @DATA
    ldr w9, [x23]
    cmp w9, #16
    mov w10, #16
    csel w9, w9, w10, le
    str w9, [x23, #0x3ec]
    ldr w8, [x23, #0x3dc]
    cmp w8, #0
    cset w25, eq
    ldr x0, [x19, #0xcd0]
    mov w1, w25
    bl show_picture
    ldr x0, [x19, #0xcf0]
    mov w1, w25
    bl show_picture
    ldr x0, [x19, #0xcd0]
    mov w1, w25
    bl show_check
    ldr x0, [x19, #0xcf0]
    mov w1, w25
    bl show_check
    mov w24, wzr
refresh_pictures:
    adrp x23, @DATA
    ldr w8, [x23, #0x3ec]
    cmp w24, w8
    b.ge refresh_pictures_done
    cbz w25, refresh_pictures_page
    ; the themes: the picture made with the entry (never freed while the page lives)
    add x2, x23, #0x1000
    add x2, x2, #0x400
    add x2, x2, x24, lsl #6
    mov x0, x19
    mov w1, w24
    bl show_holder
    mov w1, #1
    b refresh_pictures_button
refresh_pictures_page:
    ; the pages of a theme: the part's own picture, in the holders of the pages
    add x8, x23, #0x1000
    add x8, x8, #0x800
    add x8, x8, x24, lsl #5
    ldrb w8, [x8]
    mov w1, wzr
    cbz w8, refresh_pictures_button
    add x0, x23, #0x2000
    add x0, x0, #0x800
    add x0, x0, x24, lsl #6
    bl release_picture
    adrp x2, @DATA
    add x2, x2, #0x2000
    add x2, x2, #0x800
    add x2, x2, x24, lsl #6
    mov x0, x19
    mov w1, w24
    bl add_picture_at
    mov w1, #1
refresh_pictures_button:
    adrp x23, @DATA
    add x8, x23, #0x100
    add x8, x8, x24, lsl #5
    ldr x0, [x8, #0x10]
    bl show_picture
    adrp x23, @DATA
    add x8, x23, #0x100
    add x8, x8, x24, lsl #5
    ldr x0, [x8, #0x10]
    mov w1, w25
    bl show_check
    add w24, w24, #1
    b refresh_pictures
refresh_pictures_done:
    cbnz w25, refresh_checks_done
    ldr x0, [x19, #0xcd0]
    bl uncheck
    ldr x0, [x19, #0xcf0]
    bl uncheck
    mov w24, wzr
refresh_checks:
    adrp x23, @DATA
    ldr w8, [x23, #0x3e0]
    cmp w24, w8
    b.ge refresh_checks_done
    add x8, x23, #0x100
    add x8, x8, x24, lsl #5
    ldr x0, [x8, #0x10]
    bl uncheck
    add w24, w24, #1
    b refresh_checks
refresh_checks_done:
    mov x0, x19
    bl settings_style
    ldp x25, x26, [sp, #0x130]
    ldp x23, x24, [sp, #0x120]
    ldp x21, x22, [sp, #0x110]
    ldp x19, x20, [sp, #0x100]
    ldp x29, x30, [sp], #0x140
    ret

; ---------------------------------------------------------------------------------------------
; the scrolling list places entry i at (x, y): x0 = the page, w1 = i, x2 = { x, y }
place_entry:
    stp x29, x30, [sp, #-0x40]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    stp x21, x22, [sp, #0x20]
    str d8, [sp, #0x30]
    mov x19, x0
    mov w20, w1
    mov x21, x2
    bl entry_object
    mov x22, x0
    ldr x9, [x22, #8]             ; not made yet (the list moves while the page is built)
    cbz x9, place_done
    ldr x9, [x22, #0x10]
    cbz x9, place_done
    ldp s8, s0, [x21]
    ldr x9, [x22, #8]
    ldr x9, [x9, #0xa0]
    ldr x9, [x9, #0x18]
    ldrb w10, [x9, #0x58]
    stp s8, s0, [x9, #0x30]
    str wzr, [x9, #0x38]
    orr w10, w10, #0x10
    strb w10, [x9, #0x58]
    ; the button shown
    ldr x22, [x22, #0x10]
    ldr x0, [x22, #0x6b8]
    cbz x0, place_button_pane
    mov w1, wzr
    bl #0xb8ce50
    tbz w0, #0, place_line
    mov x0, x22
    mov w1, #1
    bl #0xafde10
    b place_line
place_button_pane:
    ldr x8, [x22, #0x20]
    ldr x8, [x8, #0x18]
    ldrb w9, [x8, #0x58]
    orr w9, w9, #1
    strb w9, [x8, #0x58]
    ; its line, under it
place_line:
    mov x0, x19
    mov w1, w20
    bl entry_line
    cbz x0, place_done
    ldr s0, [x21, #4]
    ldr s1, [x19, #0xd60]
    fsub s0, s0, s1
    ldr x8, [x0, #0xa0]
    ldr x8, [x8, #0x18]
    ldrb w9, [x8, #0x58]
    stp s8, s0, [x8, #0x30]
    str wzr, [x8, #0x38]
    orr w9, w9, #0x11111111
    strb w9, [x8, #0x58]
place_done:
    ldr d8, [sp, #0x30]
    ldp x21, x22, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x40
    ret

; the scrolling list moves entry i to (x, y)
move_entry:
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    str x21, [sp, #0x20]
    mov x19, x0
    mov w20, w1
    mov x21, x2
    bl entry_object
    ldr x8, [x0, #8]              ; not made yet
    cbz x8, move_done
    ldp s0, s1, [x21]
    ldr x8, [x8, #0xa0]
    ldr x8, [x8, #0x18]
    ldrb w10, [x8, #0x58]
    stp s0, s1, [x8, #0x30]
    str wzr, [x8, #0x38]
    orr w10, w10, #0x10
    strb w10, [x8, #0x58]
    mov x0, x19
    mov w1, w20
    bl entry_line
    cbz x0, move_done
    ldp s0, s1, [x21]
    ldr s2, [x19, #0xd60]
    fsub s1, s1, s2
    ldr x8, [x0, #0xa0]
    ldr x8, [x8, #0x18]
    ldrb w9, [x8, #0x58]
    stp s0, s1, [x8, #0x30]
    str wzr, [x8, #0x38]
    orr w9, w9, #0x10
    strb w9, [x8, #0x58]
move_done:
    ldr x21, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x30
    ret

; the scrolling list hides entry i
hide_entry:
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    str x21, [sp, #0x20]
    mov x19, x0
    mov w20, w1
    bl entry_object
    ldr x21, [x0, #0x10]
    cbz x21, hide_done            ; not made yet
    ldr x0, [x21, #0x6b8]
    cbz x0, hide_button_pane
    mov w1, wzr
    bl #0xb8d060
    tbz w0, #0, hide_line
    mov x0, x21
    mov w1, wzr
    bl #0xafde10
    b hide_line
hide_button_pane:
    ldr x8, [x21, #0x20]
    ldr x8, [x8, #0x18]
    ldrb w9, [x8, #0x58]
    and w9, w9, #0xfe
    strb w9, [x8, #0x58]
hide_line:
    mov x0, x19
    mov w1, w20
    bl entry_line
    cbz x0, hide_done
    ldr x8, [x0, #0xa0]
    ldr x8, [x8, #0x18]
    ldrb w9, [x8, #0x58]
    and w9, w9, #0xfe
    strb w9, [x8, #0x58]
hide_done:
    ldr x21, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x30
    ret

; ---------------------------------------------------------------------------------------------
; a layout leaves the list of what is updated and drawn (as the page does)
; x0 = the layout
unlink_layout:
    ldr x8, [x0]
    cbz x8, unlink_layout_done
    adrp x8, #0x4254000
    ldr x8, [x8, #0x2c8]
    adrp x11, #0x425a000
    ldr x11, [x11, #0xb18]
    ldr w9, [x8, #0x84]
    cmp w9, #1
    csinc w9, w9, wzr, gt
    str w9, [x8, #0x84]
    ldp x9, x8, [x0, #8]
    str xzr, [x0, #0x10]
    stp xzr, xzr, [x0]
    ldr x12, [x11, #0x660]
    cmp x12, x0
    b.ne unlink_layout_next
    str x8, [x11, #0x660]
unlink_layout_next:
    cbz x8, unlink_layout_prev
    str x9, [x8, #8]
unlink_layout_prev:
    cbz x9, unlink_layout_done
    str x8, [x9, #0x10]
unlink_layout_done:
    ret

; the page is left: the original, then the added entries leave the screen
; x0 = the page
deactivate:
    stp x29, x30, [sp, #-0x50]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    stp x21, x22, [sp, #0x20]
    stp x23, x24, [sp, #0x30]
    stp x25, x26, [sp, #0x40]
    mov x19, x0
    bl #0x98ec70

    mov w24, wzr
deactivate_entry:
    adrp x23, @DATA
    ldr w8, [x23, #0x3ec]
    cmp w24, w8
    b.ge deactivate_done
    cmp w24, #16
    b.ge deactivate_done
    ; hidden (the scrolling list may have shown it; emptied, it does not hide it)
    mov x0, x19
    add w1, w24, #2
    bl hide_entry
    adrp x23, @DATA
    ; the line off the page
    add x8, x23, #0x300
    ldr x20, [x8, x24, lsl #3]
    cbz x20, deactivate_button
    ldr x0, [x19, #0xb50]
    mov x1, x20
    bl #0xb946e0
    mov x0, x20
    bl unlink_layout
deactivate_button:
    add x25, x23, #0x100
    add x25, x25, x24, lsl #5
    ldr x0, [x25, #0x10]
    cbz x0, deactivate_layout
    adrp x20, #0x4254000
    mov x10, x0
    ldr x8, [x10, #8]!
    cbz x8, deactivate_button_off
    ldr x11, [x20, #0x400]
    ldp x9, x8, [x0, #0x10]
    stp xzr, xzr, [x10]
    str xzr, [x10, #0x10]
    ldr x12, [x11]
    cmp x12, x10
    b.ne deactivate_button_next
    str x8, [x11]
deactivate_button_next:
    cbz x8, deactivate_button_prev
    str x9, [x8, #8]
deactivate_button_prev:
    cbz x9, deactivate_button_off
    str x8, [x9, #0x10]
deactivate_button_off:
    ldr x8, [x0]
    ldr x8, [x8, #0x30]
    blr x8
deactivate_layout:
    ldr x0, [x25, #8]
    cbz x0, deactivate_next
    bl unlink_layout
deactivate_next:
    add w24, w24, #1
    b deactivate_entry
deactivate_done:
    adrp x8, @DATA
    str wzr, [x8, #0x3ec]
    ldp x25, x26, [sp, #0x40]
    ldp x23, x24, [sp, #0x30]
    ldp x21, x22, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x50
    ret

; the page is destroyed: the added entries and their lines first, then the original
; x0 = the page
destroy:
    stp x29, x30, [sp, #-0x40]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    stp x21, x22, [sp, #0x20]
    stp x23, x24, [sp, #0x30]
    mov x19, x0
    bl settings_free
    adrp x8, @DATA
    str xzr, [x8, #0x38]

    mov w24, wzr
destroy_entry:
    adrp x23, @DATA
    ldr w8, [x23, #0x3e0]
    cmp w24, w8
    b.ge destroy_page
    cmp w24, #16
    b.ge destroy_page
    ; the picture
    add x0, x23, #0x1000
    add x0, x0, #0x400
    add x0, x0, x24, lsl #6
    bl release_picture
    ; the line
    add x21, x23, #0x300
    ldr x20, [x21, x24, lsl #3]
    cbz x20, destroy_object
    mov x0, x20
    bl #0xb94400
    mov x0, x20
    bl #0xb93bb0
    mov x0, x20
    mov w1, #0x120
    bl #0x33c560
    str xzr, [x21, x24, lsl #3]
    ; the entry
destroy_object:
    add x20, x23, #0x100
    add x20, x20, x24, lsl #5
    ldr x8, [x20]
    cbz x8, destroy_next
    ldr x8, [x8, #0x48]
    mov x0, x20
    blr x8
    ldr x0, [x20, #0x10]
    cbz x0, destroy_object_end
    bl #0xafe2a0
destroy_object_end:
    ldr x8, [x20]
    ldr x8, [x8, #0x50]
    mov x0, x20
    blr x8
    stp xzr, xzr, [x20]
    stp xzr, xzr, [x20, #0x10]
destroy_next:
    add w24, w24, #1
    b destroy_entry

destroy_page:
    mov w24, wzr
destroy_page_picture:
    adrp x23, @DATA
    add x0, x23, #0x2000
    add x0, x0, #0x800
    add x0, x0, x24, lsl #6
    bl release_picture
    add w24, w24, #1
    cmp w24, #16
    b.lt destroy_page_picture
    adrp x8, @DATA
    str wzr, [x8, #0x3e0]
    mov x0, x19
    ldp x23, x24, [sp, #0x30]
    ldp x21, x22, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x40
    b #0x98e0c0


; ---------------------------------------------------------------------------------------------
; [Nextendo, HOME Menu work] The "Ryujinx | Nextendo" section (+0x3C0 set): its entries look
; like those of System ("Resolution .......... 2x"): the value on the right, in the texts of the
; check (+0x3000: 0x80 bytes per entry, UTF-16, the page's own two first), the check shown.
; x0 = the page
settings_style:
    adrp x8, @DATA
    ldr w9, [x8, #0x3c0]
    cbz w9, settings_style_done
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    str x21, [sp, #0x20]
    mov x19, x0
    ldr x0, [x19, #0xcd0]
    mov w1, #0
    bl settings_entry
    ldr x0, [x19, #0xcf0]
    mov w1, #1
    bl settings_entry
    mov w20, wzr
settings_style_entry:
    adrp x8, @DATA
    ldr w9, [x8, #0x3ec]
    cmp w20, w9
    b.ge settings_style_end
    add x8, x8, #0x100
    add x8, x8, x20, lsl #5
    ldr x0, [x8, #0x10]
    add w1, w20, #2
    bl settings_entry
    add w20, w20, #1
    b settings_style_entry
settings_style_end:
    ldr x21, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x30
settings_style_done:
    ret

; one entry: x0 = its button, w1 = its row (the value at +0x3000 + 0x80 * row)
settings_entry:
    cbz x0, settings_entry_done
    stp x29, x30, [sp, #-0x20]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    mov x19, x0
    adrp x20, @DATA
    add x20, x20, #0x3000
    add x20, x20, x1, lsl #7
    mov w1, #0
    bl show_check
    mov x0, x19
    mov w1, #0
    bl show_picture
    mov x0, x19
    adr x1, name_value
    mov x2, x20
    bl set_named_text
    mov x0, x19
    mov w1, #1
    adr x2, name_value
    bl show_pane
    ; the name where the picture was: N_LPos (x 105) at 0
    ldr x8, [x19, #0x20]
    ldr x0, [x8, #0x18]
    adr x1, name_lpos
    bl #0xb94e20
    cbz x0, settings_entry_end
    mov w9, #0x42d20000
    mov x10, #0
settings_entry_lpos:
    cmp x10, #0x80
    b.ge settings_entry_end
    ldr w11, [x0, x10]
    cmp w11, w9
    b.ne settings_entry_lpos_next
    str wzr, [x0, x10]
    b settings_entry_end
settings_entry_lpos_next:
    add x10, x10, #4
    b settings_entry_lpos
settings_entry_end:
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x20
settings_entry_done:
    ret

; a text of an entry: x0 = its button, x1 = the pane's name, x2 = the text (UTF-16)
set_named_text:
    cbz x0, set_named_text_done
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    str x19, [sp, #0x20]
    mov x19, x2
    ldr x8, [x0, #0x20]
    ldr x0, [x8, #0x18]
    bl #0xb94e20
    cbz x0, set_named_text_end
    str x0, [sp, #0x10]
    str x19, [sp, #0x18]
    add x0, sp, #0x10
    add x1, sp, #0x18
    bl #0xb98e80
set_named_text_end:
    ldr x19, [sp, #0x20]
    ldp x29, x30, [sp], #0x30
set_named_text_done:
    ret


; ---------------------------------------------------------------------------------------------
; [Nextendo, HOME Menu work] The select boxes of the "Ryujinx | Nextendo" section: the system's
; own (those of System: Region, Language), one per setting, made as System makes them
; (0x890670): a manager (0x400 bytes, 0xB10E60 / 0xB10FD0), then each box (0x178 bytes, its
; fields, 0xB15230 with its parameters, 0xB15820 per choice, 0xB18BC0 its title, 0xB15720).
; Opened as System opens Region's (0x8955C0): the page's select box request (+0xBB8, made by
; the page's base when vtable +0x420 says so) given the box, and the request to the system.
; Data page (written by the emulator):
;   +0x3A00 u64   the manager; +0x3A08 u64 per setting: its box
;   +0x3B00 u32   number of settings; +0x3B04 u8 per setting: its number of choices
;   +0x3B40 u32   per setting: its choice in use
;   +0x4000 0x40 bytes per setting: u64 pointers to its title's label, then its choices'
;   +0x03C4 u32   the setting whose box to open, plus one (the emulator, on A); 0 when done
;   +0x03C8 s32   the choice made, +0x03CC u32 its setting (then "NxSettng" to the emulator)

; vtable +0x420: the page uses select boxes (in the "Ryujinx | Nextendo" section)
uses_select_boxes:
    adrp x8, @DATA
    ldr w0, [x8, #0x3c0]
    cmp w0, #0
    cset w0, ne
    ret

; the boxes made: x0 = the page
settings_build:
    stp x29, x30, [sp, #-0x1a0]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    stp x21, x22, [sp, #0x20]
    stp x23, x24, [sp, #0x30]
    stp x25, x26, [sp, #0x40]
    stp x27, x28, [sp, #0x50]
    mov x19, x0
    adrp x28, @DATA
    ldr x20, [x28, #8]            ; the build argument: the allocator's holder
    ; the manager
    ldr x0, [x20]
    ldr x8, [x0]
    ldr x8, [x8, #0x10]
    mov w1, #0x400
    mov w2, #8
    blr x8
    cbz x0, settings_build_end
    mov x21, x0
    bl #0xb10e60
    add x8, x28, #0x3000
    str x21, [x8, #0xa00]
    add x1, sp, #0x90
    str x20, [x1]
    str wzr, [x1, #8]
    str xzr, [x1, #0x10]
    adrp x9, #0xef9000
    add x9, x9, #0x4c
    str x9, [x1, #0x18]
    str x9, [x1, #0x20]
    str wzr, [x1, #0x28]
    mov x0, x21
    bl #0xb10fd0
    mov w22, wzr
settings_build_box:
    add x8, x28, #0x3000
    ldr w9, [x8, #0xb00]
    cmp w22, w9
    b.ge settings_build_end
    cmp w22, #16
    b.ge settings_build_end
    ; the box
    ldr x0, [x20]
    ldr x8, [x0]
    ldr x8, [x8, #0x10]
    mov w1, #0x178
    mov w2, #8
    blr x8
    cbz x0, settings_build_end
    mov x23, x0
    bl select_box_fields
    add x8, x28, #0x3000
    add x8, x8, #0xa08
    str x23, [x8, x22, lsl #3]
    ; its parameters (0x90 bytes at sp+0xC0)
    add x1, sp, #0xc0
    mov x9, #0
settings_build_zero:
    str xzr, [x1, x9]
    add x9, x9, #8
    cmp x9, #0x90
    b.lt settings_build_zero
    str x20, [x1]
    add x8, x28, #0x3000
    add x8, x8, #0xb04
    ldrb w9, [x8, x22]
    str w9, [x1, #8]
    adrp x9, #0x108a000
    add x9, x9, #0x988            ; an empty function
    str x9, [x1, #0x10]
    add x10, x1, #0x20
    str x10, [x1, #0x18]
    str x9, [x1, #0x40]
    adrp x9, #0x1085000
    add x9, x9, #0x288            ; a method of the page
    str x9, [x1, #0x20]
    str x19, [x1, #0x28]
    adr x9, settings_results
    add x9, x9, x22, lsl #3
    str x9, [x1, #0x30]
    adrp x9, #0xef9000
    add x9, x9, #0x4c
    str x9, [x1, #0x78]
    add x8, x28, #0x3000
    ldr x9, [x8, #0xa00]
    str x9, [x1, #0x88]
    mov x0, x23
    bl #0xb15230
    ; its choices
    mov w24, wzr
settings_build_choice:
    add x8, x28, #0x3000
    add x8, x8, #0xb04
    ldrb w9, [x8, x22]
    cmp w24, w9
    b.ge settings_build_title
    cmp w24, #7
    b.ge settings_build_title
    add x8, x28, #0x4000
    add x8, x8, x22, lsl #6
    add x8, x8, #8
    ldr x9, [x8, x24, lsl #3]
    add x1, sp, #0x70
    str x9, [x1]
    adrp x9, #0xef9000
    add x9, x9, #0x4a
    str x9, [x1, #8]
    strb wzr, [x1, #0x10]
    mov x0, x23
    bl #0xb15820
    add w24, w24, #1
    b settings_build_choice
settings_build_title:
    add x8, x28, #0x4000
    add x8, x8, x22, lsl #6
    ldr x1, [x8]
    ldr x0, [x23, #0x38]
    cbz x0, settings_build_finish
    bl #0xb18bc0
settings_build_finish:
    mov x0, x23
    bl #0xb15720
    add w22, w22, #1
    b settings_build_box
settings_build_end:
    ldp x27, x28, [sp, #0x50]
    ldp x25, x26, [sp, #0x40]
    ldp x23, x24, [sp, #0x30]
    ldp x21, x22, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x1a0
    ret

; a new box's fields: x0 = the box
select_box_fields:
    adrp x8, #0x10e2000
    add x8, x8, #0x258
    str x8, [x0]
    str xzr, [x0, #8]
    str wzr, [x0, #0x10]
    stp xzr, xzr, [x0, #0x18]
    stp xzr, xzr, [x0, #0x28]
    stp xzr, xzr, [x0, #0x38]
    adrp x8, #0x10e1000
    add x8, x8, #0x478
    stp xzr, x8, [x0, #0x48]
    stp xzr, xzr, [x0, #0x58]
    strb wzr, [x0, #0x68]
    adrp x9, #0x108a000
    add x9, x9, #0x988
    stp x9, xzr, [x0, #0x70]
    stp x9, xzr, [x0, #0xa0]
    stp x9, xzr, [x0, #0xd0]
    stp x9, xzr, [x0, #0x100]
    stp x9, xzr, [x0, #0x130]
    str wzr, [x0, #0x160]
    add x8, x0, #0x160
    stur wzr, [x8, #3]
    mov w8, #0x101
    add x9, x0, #0x167
    strh w8, [x9]
    mov x8, #0x7fffffff00000000
    add x9, x0, #0x16c
    str x8, [x9]
    strb wzr, [x0, #0x174]
    ret

; once a frame: the restart dialog (asked by the emulator, or its answer), then a box to open
; (asked by the emulator): its choice in use ticked, then shown
settings_poll:
    stp x29, x30, [sp, #-0x10]!
    mov x29, sp
    bl restart_dialog_poll
    ldp x29, x30, [sp], #0x10
    adrp x8, @DATA
    ldr w9, [x8, #0x3c4]
    cbz w9, settings_poll_done
    str wzr, [x8, #0x3c4]
    sub w9, w9, #1
    cmp w9, #16
    b.ge settings_poll_done
    add x10, x8, #0x3000
    add x10, x10, #0xa08
    ldr x0, [x10, x9, lsl #3]
    cbz x0, settings_poll_done
    add x10, x8, #0x3000
    add x10, x10, #0xb40
    ldr w1, [x10, x9, lsl #2]
    ; the tick: every choice cleared, the one in use set (as System does for Region)
    ldr x11, [x0, #0x40]
    ldr x12, [x11, #0x70]
    ldrb w13, [x11, #0x80]
    tbnz w13, #0, settings_poll_tick
    ldr w13, [x12, #0x10]
    mov w14, wzr
settings_poll_clear:
    cmp w14, w13
    b.ge settings_poll_tick
    ldp w15, w16, [x12, #8]
    add w17, w14, w16
    cmp w17, w15
    csel w15, wzr, w15, lt
    sub w17, w17, w15
    mov w15, #0x28
    smull x17, w17, w15
    ldr x15, [x12]
    strb wzr, [x15, x17]
    add w14, w14, #1
    b settings_poll_clear
settings_poll_tick:
    ldp w15, w16, [x12, #8]
    add w17, w1, w16
    cmp w17, w15
    csel w15, wzr, w15, lt
    sub w17, w17, w15
    mov w15, #0x28
    smull x17, w17, w15
    ldr x15, [x12]
    mov w13, #1
    strb w13, [x15, x17]
    str w1, [x12, #0x30]
    ; the request: the page's (+0xBB8), free, given the box, then to the system
    ldr x10, [x8, #0x38]
    cbz x10, settings_poll_done
    ldr x11, [x10, #0xbb8]
    cbz x11, settings_poll_done
    ldr w12, [x11, #0x30]
    cbnz w12, settings_poll_done
    str x0, [x11]
    mov w12, #1
    str w12, [x11, #0x30]
    adrp x13, #0x4254000
    ldr x13, [x13, #0x558]
    str x11, [x13]
settings_poll_done:
    ret

; a choice made in a box (x0 = the page, w1 = its setting, w2 = the choice, -1 when left)
settings_result:
    cmn w2, #1
    b.eq settings_result_done
    adrp x8, @DATA
    str w2, [x8, #0x3c8]
    str w1, [x8, #0x3cc]
    adr x0, settings_signal
    mov x1, #8
    svc #0x27
settings_result_done:
    ret

; one entry per setting (its number given to settings_result)
settings_results:
    mov w1, #0
    b settings_result
    mov w1, #1
    b settings_result
    mov w1, #2
    b settings_result
    mov w1, #3
    b settings_result
    mov w1, #4
    b settings_result
    mov w1, #5
    b settings_result
    mov w1, #6
    b settings_result
    mov w1, #7
    b settings_result
    mov w1, #8
    b settings_result
    mov w1, #9
    b settings_result
    mov w1, #10
    b settings_result
    mov w1, #11
    b settings_result
    mov w1, #12
    b settings_result
    mov w1, #13
    b settings_result
    mov w1, #14
    b settings_result
    mov w1, #15
    b settings_result

; the boxes and the manager freed with the page (as System frees its own, 0x8935F8)
settings_free:
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    str x21, [sp, #0x20]
    adrp x19, @DATA
    mov w20, wzr
settings_free_box:
    cmp w20, #16
    b.ge settings_free_manager
    add x8, x19, #0x3000
    add x8, x8, #0xa08
    ldr x21, [x8, x20, lsl #3]
    str xzr, [x8, x20, lsl #3]
    cbz x21, settings_free_next
    mov x0, x21
    bl #0xb15460
    mov x0, x21
    bl #0xb14ff0
    mov x0, x21
    mov w1, #0x178
    bl #0x33c560
settings_free_next:
    add w20, w20, #1
    b settings_free_box
settings_free_manager:
    add x8, x19, #0x3000
    ldr x21, [x8, #0xa00]
    str xzr, [x8, #0xa00]
    cbz x21, settings_free_done
    ; as System (0x89365C): its object told first, then deleted, then the manager's parts
    ldr x0, [x21]
    cbz x0, settings_free_manager_parts
    ldr x8, [x0]
    ldr x8, [x8, #0x18]
    blr x8
    ldr x0, [x21]
    str xzr, [x21]
    cbz x0, settings_free_manager_parts
    ldr x8, [x0]
    ldr x8, [x8, #8]
    blr x8
settings_free_manager_parts:
    add x0, x21, #0x230
    bl #0xb2c6f0
    add x0, x21, #8
    bl #0xb3b3f0
settings_free_manager_mem:
    mov x0, x21
    mov w1, #0x400
    bl #0x33c560
settings_free_done:
    ldr x21, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x30
    ret


; ---------------------------------------------------------------------------------------------
; [Nextendo, HOME Menu work] The restart dialog of the "Ryujinx | Nextendo" section: settings
; read when the emulation starts were changed, the user leaves the section (B): the system's
; dialog, as System's for a new language (0x894C80): its parameters (0x170 bytes), the body's
; label (0xB1BE00: its attributes give the buttons), two callbacks, shown by the dialog manager
; (0x4254550, 0xB21640). The second callback gets the dialog: its state (+0x24, 4 when over) and
; the button (+0xAF0, 0 to go on) are read once a frame (as System does).
; Data page:
;   +0x6000 the callbacks' vtables (written by the emulator: 6 u64 each, the second at +0x30)
;   +0x6100 u64  the dialog (from the second callback)
;   +0x6108 s32  the button (from the first), +0x610C u32 the dialog is asked (by the emulator)
;   +0x6110 u64  the body's label (a pointer, written by the emulator)

restart_dialog_poll:
    sub sp, sp, #0x240
    stp x29, x30, [sp]
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    stp x21, x22, [sp, #0x20]
    adrp x20, @DATA
    add x20, x20, #0x6000
    ; Cancel was picked (+0x114): the dialog, once over (state 4), is closed as System closes
    ; its own (0x7D7B60: the dialog on top, state 7, its close), until it is (state above 4)
    ldr w8, [x20, #0x114]
    cbz w8, restart_dialog_no_close
    bl #0x7d7b60
    ldr x9, [x20, #0x100]
    cbz x9, restart_dialog_closed
    ldr w8, [x9, #0x24]
    cmp w8, #5
    b.lt restart_dialog_no_close
restart_dialog_closed:
    str wzr, [x20, #0x114]
    str xzr, [x20, #0x100]
restart_dialog_no_close:
    ldr w8, [x20, #0x10c]
    cbz w8, restart_dialog_end
    str wzr, [x20, #0x10c]
    adrp x8, @DATA
    ldr x19, [x8, #0x38]          ; the page
    cbz x19, restart_dialog_end
    ; the parameters (S at sp+0x40, 0x170 bytes), zero first
    add x21, sp, #0x40
    mov x9, #0
restart_dialog_zero:
    str xzr, [x21, x9]
    add x9, x9, #8
    cmp x9, #0x170
    b.lt restart_dialog_zero
    mov x9, #0x100000000
    str x9, [x21, #0x10]
    adrp x9, #0xef9000
    add x9, x9, #0x4c             ; ""
    stp x9, x9, [x21, #0x38]
    stp x9, x9, [x21, #0x48]
    str x9, [x21, #0x148]
    mov w9, #1
    strb w9, [x21, #0x140]
    adrp x9, #0x108a000
    add x9, x9, #0x988            ; the callbacks' holder
    str x9, [x21, #0x80]
    str x9, [x21, #0xb0]
    str x9, [x21, #0xe0]
    str x9, [x21, #0x110]
    str x9, [x21, #0x158]
    ; the first callback (the buttons), inline
    add x10, x21, #0x90
    str x10, [x21, #0x88]
    str x20, [x10]                ; vtable +0x6000
    str x19, [x10, #8]
    ; the second (the dialog), inline
    add x10, x21, #0x160
    add x10, x10, #8
    str x10, [x21, #0x160]
    add x11, x20, #0x30
    str x11, [x10]                ; vtable +0x6030
    str x19, [x10, #8]
    ; the body
    mov x0, x21
    ldr x1, [x20, #0x110]
    cbz x1, restart_dialog_end
    bl #0xb1be00
    ; the page's defaults (as System's)
    ldr x8, [x21]
    cbnz x8, restart_dialog_d1
    ldr x8, [x19, #0x50]
    add x8, x8, #0x28
    str x8, [x21]
restart_dialog_d1:
    ldr x8, [x21, #0x78]
    cbnz x8, restart_dialog_d2
    add x8, x19, #0x488
    str x8, [x21, #0x78]
restart_dialog_d2:
    ldr x8, [x21, #0x70]
    cbnz x8, restart_dialog_show
    add x8, x19, #0x2b8
    str x8, [x21, #0x70]
restart_dialog_show:
    ; the dialog callback once more, for the manager (F at sp+0x1C0)
    add x2, sp, #0x1c0
    adrp x9, #0x108a000
    add x9, x9, #0x988
    str x9, [x2]
    add x10, x2, #0x10
    str x10, [x2, #8]
    add x11, x20, #0x30
    str x11, [x10]
    str x19, [x10, #8]
    adrp x8, #0x4254000
    ldr x0, [x8, #0x550]
    cbz x0, restart_dialog_end
    mov x1, x21
    bl #0xb21640
restart_dialog_end:
    ldp x21, x22, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp]
    add sp, sp, #0x240
    ret

; the callbacks: copy (x0 = this, x1 = where), then call
restart_callback_copy:
    cbz x1, restart_callback_copy_done
    ldr x8, [x0]
    str x8, [x1]
    ldr x8, [x0, #8]
    str x8, [x1, #8]
restart_callback_copy_done:
    mov x0, x1
    ret

; the button chosen (w1), told to the emulator (as System's, 0x899230, takes it)
restart_callback_buttons:
    adrp x8, @DATA
    add x8, x8, #0x6000
    str w1, [x8, #0x108]
    str w1, [x8, #0x114]          ; Cancel (1): the dialog is to be closed
    adr x0, restart_signal
    mov x1, #8
    svc #0x27
    mov w0, #1                    ; the dialog closes
    ret

; the dialog (x1): kept, its answer read once a frame
restart_callback_dialog:
    adrp x8, @DATA
    add x8, x8, #0x6000
    str x1, [x8, #0x100]
    ret

; ---------------------------------------------------------------------------------------------
; [Nextendo, HOME Menu work] The welcome of the virtual game cards page (two dialogs) only the
; first time: the page's welcome step (0xA5F450) shows its dialog when none is open (w8 = 0);
; once the emulator says it was seen (data page +0x6300, set when the page says so, ns 4073),
; the step goes on at once to the next one (as when its dialog is over).
vgc_welcome:
    cbnz w8, vgc_welcome_open
    adrp x9, @DATA
    add x9, x9, #0x6000
    ldrb w9, [x9, #0x300]
    cbnz w9, vgc_welcome_skip
    b #0xa5f47c
vgc_welcome_open:
    b #0xa5f68c
vgc_welcome_skip:
    b #0xa5f734

name_value:
    .byte 84, 95, 86, 97, 108, 117, 101, 0
name_lpos:
    .byte 78, 95, 76, 80, 111, 115, 0
    .byte 0, 0, 0, 0, 0, 0, 0, 0

; the name of the "Ryujinx | Nextendo" button (a label added to setting.msbt)
nav_label:
    .byte 78, 120, 95, 83, 101, 116, 78, 97, 118, 95, 82, 121, 117, 106, 105, 110, 120, 0
settings_signal:
    .byte 78, 120, 83, 101, 116, 116, 110, 103
restart_signal:
    .byte 78, 120, 83, 101, 116, 82, 115, 116
