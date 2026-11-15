#ifndef _SYS_TYPES_H
#define _SYS_TYPES_H

#include <stdint.h>
#include <stddef.h>

typedef int32_t   pid_t;
typedef int32_t   uid_t;
typedef int32_t   gid_t;
typedef int64_t   off_t;
typedef int64_t   off64_t;
typedef int64_t   ssize_t;
typedef uint32_t  mode_t;
typedef uint64_t  ino_t;
typedef uint64_t  dev_t;
typedef uint32_t  nlink_t;
typedef int64_t   time_t;
typedef int64_t   suseconds_t;
typedef uint32_t  useconds_t;
typedef int64_t   clock_t;
typedef int32_t   clockid_t;
typedef intptr_t  intptr_t;
typedef uintptr_t uintptr_t;

#endif // _SYS_TYPES_H
