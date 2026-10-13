@echo off
rem Usage: build.bat hello      (builds hello.c -> hello.elf)
rem Needs LLVM: winget install LLVM.LLVM   (clang + ld.lld). NOT MinGW/MSVC - those make .exe, not ELF.
rem (Not tested on Windows - if it fails, use WSL + build.sh instead.)
if "%1"=="" (echo usage: build.bat NAME & exit /b 1)
clang --target=x86_64-unknown-none-elf -c %1.c -o %1.o -ffreestanding -fno-builtin -fPIE -fvisibility=hidden -fno-stack-protector -ffunction-sections -mno-red-zone -fno-asynchronous-unwind-tables -O2 -Wall || exit /b 1
ld.lld -T link.ld -static -z norelro -z noseparate-code --build-id=none -o %1.elf %1.o || exit /b 1
del %1.o
echo built %1.elf
