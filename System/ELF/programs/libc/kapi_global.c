#include "kernel.h"

// Global KernelApi pointer shared across all dynamic modules
__attribute__((visibility("default"))) KernelApi *__k = 0;
