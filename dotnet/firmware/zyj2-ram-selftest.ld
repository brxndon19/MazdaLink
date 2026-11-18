OUTPUT_FORMAT("elf32-sh")
OUTPUT_ARCH(sh)
ENTRY(ram_selftest)
SECTIONS
{
    . = 0xffff8000;
    .text : { *(.text) }
    /DISCARD/ : { *(.data) *(.bss) *(.comment) *(.note*) }
}
