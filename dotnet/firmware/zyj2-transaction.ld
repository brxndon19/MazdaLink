OUTPUT_FORMAT("elf32-sh")
OUTPUT_ARCH(sh)
ENTRY(ram_selftest)
SECTIONS
{
 . = 0xffff8000;
 .text : { KEEP(*(.text.entry)) *(.text.handlers) *(.text.service) *(.text*) *(.rodata*) }
 ASSERT(. <= 0xffffa000, "Kernel overlaps fixed staging buffer")
 /DISCARD/ : { *(.data*) *(.bss*) *(.comment*) *(.note*) *(.eh_frame*) }
}
