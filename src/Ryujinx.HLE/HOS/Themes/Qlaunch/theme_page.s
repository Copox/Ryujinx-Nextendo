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
;   +0x040  char  entry template layout name
;   +0x080  char  line template layout name
;   +0x100  0x20 bytes per added entry: its object
;   +0x300  u64   per added entry: its line
;   +0x400  0x100 bytes per added entry: its name (UTF-16)
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
    bl #0x98ceb0                  ; Basic White, Basic Black

    adrp x23, @DATA
    ldr w8, [x23]
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
    ldr w8, [x23]
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
    add w24, w24, #1
    b build_entry

build_done:
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
    ldp s0, s1, [x21]
    ldr x8, [x0, #8]
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
    ldr w8, [x23]
    cmp w24, w8
    b.ge deactivate_done
    cmp w24, #16
    b.ge deactivate_done
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

    mov w24, wzr
destroy_entry:
    adrp x23, @DATA
    ldr w8, [x23]
    cmp w24, w8
    b.ge destroy_page
    cmp w24, #16
    b.ge destroy_page
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
    mov x0, x19
    ldp x23, x24, [sp, #0x30]
    ldp x21, x22, [sp, #0x20]
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x40
    b #0x98e0c0
