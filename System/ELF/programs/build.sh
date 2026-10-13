#!/bin/sh
# Usage: sh build.sh hello        (builds hello.c -> hello.elf)
#        sh build.sh              (builds every .c in this folder)
# Needs gcc + binutils on x86-64 Linux (WSL / Docker / VM is fine).
set -e
cd "$(dirname "$0")"
build() {
    gcc -c "$1.c" -o "$1.o" \
        -ffreestanding -fno-builtin -fno-tree-loop-distribute-patterns \
        -fPIE -fvisibility=hidden -fno-stack-protector -ffunction-sections \
        -mno-red-zone -fno-asynchronous-unwind-tables -O2 -Wall
    ld -T link.ld -static -nostdlib -z norelro -z noseparate-code --build-id=none \
        -o "$1.elf" "$1.o" 2>&1 | grep -v "RWX permissions" || true
    rm -f "$1.o"
    echo "built $1.elf"
}
if [ -n "$1" ]; then build "$1"; else for f in *.c; do build "${f%.c}"; done; fi
