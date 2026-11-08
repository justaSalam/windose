# SDD ledger — plan: docs/superpowers/plans/2026-11-08-system-stability-and-elf-prioritization.md

Pre-flight: Interfaces checked. Task 1-7 proceed sequentially.
Ruling: calc.c uses fixed-size window (canResize=false) matching standard Windows Calculator conventions.
Ruling: ps_list unifies ProcessManager.processes using Process.id and protects canTerminate==false processes.
Ruling: reg_list added to KernelApi for key enumeration, reg_set performs type coercion.
Ruling: aeroeditor.c updated to load argv[1] and perform real file read/write.
Task 1: complete — commit 5fd9a9a
Task 2: complete — commit fc4f862
Task 3: complete — commit bf54434
Task 4: complete — commit e6f1bc1
Task 5: complete — commit d8034e1
Task 6: complete — commit 1d2055e
Task 7: complete — commit dab2bab
