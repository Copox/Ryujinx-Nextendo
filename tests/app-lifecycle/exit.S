// Original guest: debug-string syscall and immediate ExitProcess; no Nintendo SDK.
.text
.global _start
_start:
 b start
 .word 0
 .quad 0
 .ascii "NRO0"
 .word 0
 .word 0x3000
 .word 0
 .word 0,0x1000,0x1000,0x1000,0x2000,0x1000
 .word 0,0
 .zero 32
 .zero 32
start:
 adr x0, msg
 mov x1, msg_end-msg
 svc #0x27
 svc #0x07
 b .
msg:
 .ascii "EXIT-GUEST reached\n"
msg_end:
 .org 0x3000
 .ascii "ASET"
 .word 0
 .quad 0,0,0,0,0,0
