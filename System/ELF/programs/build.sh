#!/bin/sh
# Builds libc.a and the programs in src/ into bin/ (position-independent ELF, ET_DYN).
#   sh build.sh            build everything
#   sh build.sh hello      build only src/hello.c
# Needs gcc + binutils on x86-64 Linux (WSL / Docker / VM is fine).
set -e
cd "$(dirname "$0")"
mkdir -p bin obj

GCCINC=$(gcc -print-file-name=include)
CFLAGS="-c -O2 -Wall -Wno-unused-function -Wno-infinite-recursion -ffreestanding -fno-builtin -fno-tree-loop-distribute-patterns \
 -fPIE -fvisibility=hidden -fno-stack-protector -fcf-protection=none -ffunction-sections -fdata-sections \
 -mno-red-zone -fno-asynchronous-unwind-tables -nostdinc -isystem include -isystem $GCCINC"
LDFLAGS="-pie --no-dynamic-linker -e _start -z norelro -z noseparate-code -z max-page-size=0x1000 \
 -z common-page-size=0x1000 --build-id=none --gc-sections"

rm -f obj/*.o libc.a
for f in libc/*.c; do gcc $CFLAGS "$f" -o "obj/$(basename "${f%.c}").o"; done
ar rcs libc.a $(ls obj/*.o | grep -v crt0.o)

build() {
    gcc $CFLAGS "src/$1.c" -o "obj/prog_$1.o"
    ld $LDFLAGS -o "bin/$1.elf" obj/crt0.o "obj/prog_$1.o" libc.a
    echo "built bin/$1.elf"
}
if [ -n "$1" ]; then build "$1"; else for f in src/*.c; do build "$(basename "${f%.c}")"; done; fi
