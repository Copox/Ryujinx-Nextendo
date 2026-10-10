; [Nextendo, HOME Menu themes] Code added to the HOME Menu (qlaunch 22.5.0): the themes installed
; on the SD card are listed in "System Settings > Themes" after Basic White and Basic Black.
; Addresses are offsets in the module (the image as loaded); the emulator puts this at 0x425F000
; and its data page at 0x4267000.
;
; Entry points (the table below, 4 bytes each):
;   +0  build      the page's "build" method (vtable slot 0x10D4020): the original, then ours

    b build

; ---- the page is built ----
; x0 = the page, x1 = the build argument
build:
    stp x29, x30, [sp, #-0x30]!
    mov x29, sp
    stp x19, x20, [sp, #0x10]
    mov x19, x0
    mov x20, x1
    bl #0x98ceb0                  ; the page's own build: Basic White, Basic Black
    adr x0, msg_built
    mov x1, #22
    svc #0x27                     ; OutputDebugString
    ldp x19, x20, [sp, #0x10]
    ldp x29, x30, [sp], #0x30
    ret

msg_built:
    .ascii "[theme] page built !!!"
    .align 2
