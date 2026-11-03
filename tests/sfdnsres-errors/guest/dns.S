// Original minimal ARM64 guest. No Nintendo SDK, libnx or game code.
// It uses the CMIF/MapAlias layout handled by IpcMessage/IpcBuffDesc.+.text
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
    mrs x19,tpidrro_el0
    mov w28,0
    adr x0,begin_msg
    mov x1,begin_end-begin_msg
    svc #0x27
    adr x1,sm_name
    svc #0x1f
    cbnz w0,failed_setup
    mov w20,w1
    bl clear_ipc
    mov w8,4
    str w8,[x19]
    mov w8,10
    str w8,[x19,#4]
    mov w8,0x4653
    movk w8,0x4943,lsl #16
    str w8,[x19,#0x10]
    mov w0,w20
    svc #0x21
    cbnz w0,failed_setup
    ldr w0,[x19,#0x18]
    cbnz w0,failed_setup
    bl clear_ipc
    mov w8,4
    str w8,[x19]
    mov w8,10
    str w8,[x19,#4]
    mov w8,0x4653
    movk w8,0x4943,lsl #16
    str w8,[x19,#0x10]
    mov w8,1
    str w8,[x19,#0x18]
    adr x9,resolver_name
    ldr x9,[x9]
    str x9,[x19,#0x20]
    mov w0,w20
    svc #0x21
    cbnz w0,failed_setup
    ldr w0,[x19,#0x18]
    cbnz w0,failed_setup
    ldr w21,[x19,#0x0c]

    adr x0,invalid_forward
    mov x1,invalid_forward_end-invalid_forward
    svc #0x27
    adr x22,invalid_name
    mov x23,invalid_name_end-invalid_name
    mov w24,1
    mov w25,7
    mov w26,2
    bl lookup

    adr x0,localhost_forward
    mov x1,localhost_forward_end-localhost_forward
    svc #0x27
    adr x22,localhost_name
    mov x23,localhost_name_end-localhost_name
    mov w24,0
    mov w25,0
    mov w26,2
    bl lookup

    adr x0,invalid_addrinfo
    mov x1,invalid_addrinfo_end-invalid_addrinfo
    svc #0x27
    adr x22,invalid_name
    mov x23,invalid_name_end-invalid_name
    mov w24,1
    mov w25,7
    mov w26,6
    bl lookup

    adr x0,summary_msg
    mov x1,summary_end-summary_msg
    svc #0x27
    mov w0,w28
    bl print_hex
    svc #0x07
    b .

failed_setup:
    mov w27,w0
    adr x0,setup_msg
    mov x1,setup_end-setup_msg
    svc #0x27
    mov w0,w27
    bl print_hex
    svc #0x07
    b .

clear_ipc:
    mov x9,x19
    mov w10,16
1:  stp xzr,xzr,[x9],#16
    subs w10,w10,#1
    b.ne 1b
    ret

// x0 address, w1 size, x2 descriptor destination.
descriptor:
    str w1,[x2]
    str w0,[x2,#4]
    ubfx x9,x0,#32,#4
    lsl w9,w9,#28
    ubfx x10,x0,#36,#3
    orr w9,w9,w10,lsl #2
    str w9,[x2,#8]
    ret

lookup:
    stp x29,x30,[sp,#-16]!
    bl clear_ipc
    mov w8,4
    movk w8,0x110,lsl #16
    cmp w26,6
    b.ne 2f
    movk w8,0x130,lsl #16
2:  str w8,[x19]
    mov w8,12
    str w8,[x19,#4]
    mov x0,x22
    mov w1,w23
    add x2,x19,#8
    bl descriptor
    cmp w26,6
    b.eq 3f
    adr x0,response_buffer
    mov w1,0x200
    add x2,x19,#20
    bl descriptor
    mov x12,0x20
    b 4f
3:  adr x0,empty_name
    mov w1,1
    add x2,x19,#20
    bl descriptor
    adr x0,response_buffer
    mov w1,0
    add x2,x19,#32
    bl descriptor
    adr x0,response_buffer
    mov w1,0x200
    add x2,x19,#44
    bl descriptor
    mov x12,0x40
4:  mov w8,0x4653
    movk w8,0x4943,lsl #16
    str w8,[x19,x12]
    add x13,x12,#8
    str w26,[x19,x13]
    mov w0,w21
    svc #0x21
    cbnz w0,lookup_failure
    ldr w0,[x19,#0x18]
    cbnz w0,lookup_failure
    ldr w27,[x19,#0x20]
    cmp w27,w24
    cinc w28,w28,ne
    mov w0,w27
    bl print_hex
    ldr w27,[x19,#0x24]
    cmp w27,w25
    cinc w28,w28,ne
    mov w0,w27
    bl print_hex
    ldr w0,[x19,#0x28]
    bl print_hex
    ldp x29,x30,[sp],#16
    ret
lookup_failure:
    add w28,w28,#1
    bl print_hex
    ldp x29,x30,[sp],#16
    ret

print_hex:
    adr x10,hex_line
    add x11,x10,#18
    mov w12,28
    mov w13,8
5:  lsr w14,w0,w12
    and w14,w14,#15
    cmp w14,10
    add w15,w14,#48
    add w16,w14,#87
    csel w15,w15,w16,lt
    strb w15,[x11],#1
    sub w12,w12,#4
    subs w13,w13,#1
    b.ne 5b
    adr x0,hex_line
    mov x1,hex_line_end-hex_line
    svc #0x27
    ret

sm_name: .asciz "sm:"
resolver_name: .ascii "sfdnsres"
invalid_name: .asciz "invalid name with spaces"
invalid_name_end:
localhost_name: .asciz "localhost"
localhost_name_end:
empty_name: .byte 0
begin_msg: .ascii "DNS-GUEST begin actual emulated IPC\n"
begin_end:
setup_msg: .ascii "DNS-GUEST SETUP FAILURE; result follows\n"
setup_end:
invalid_forward: .ascii "DNS-GUEST forward invalid: netdb,gai,size follow\n"
invalid_forward_end:
localhost_forward: .ascii "DNS-GUEST forward localhost: netdb,gai,size follow\n"
localhost_forward_end:
invalid_addrinfo: .ascii "DNS-GUEST addrinfo invalid: netdb,gai,size follow\n"
invalid_addrinfo_end:
summary_msg: .ascii "DNS-GUEST total mismatches follows (expected zero)\n"
summary_end:
.org 0x2000
hex_line: .ascii "DNS-GUEST value=0x00000000\n"
hex_line_end:
.balign 16
response_buffer: .zero 0x200
.org 0x3000
.ascii "ASET"
.word 0
.quad 0,0,0,0,0,0
