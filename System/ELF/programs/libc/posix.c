// posix.c - Comprehensive POSIX and Linux C Library Compatibility Layer for Windose.
// Implements standard file descriptors, POSIX I/O, mmap, process info, directory enumeration,
// stack protector, dynamic linker shims (dlopen/dlsym), and glibc entry (__libc_start_main).

#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <errno.h>

#include <sys/types.h>
#include <sys/stat.h>
#include <sys/mman.h>
#include <sys/ioctl.h>
#include <sys/utsname.h>
#include <unistd.h>
#include <fcntl.h>
#include <dirent.h>
#include <dlfcn.h>

#include "kernel.h"

#define EXPORT __attribute__((visibility("default")))

// ---- Environment and Globals ----
static char *default_env[] = {
    "PATH=/mnt/Programs;/mnt/System",
    "USER=root",
    "HOME=/mnt",
    "PWD=/mnt",
    "OS=Windose",
    "TERM=windose",
    NULL
};

EXPORT char **environ = default_env;

// Stack protector canary and failure handler (required by GCC/Clang -fstack-protector)
EXPORT uintptr_t __stack_chk_guard = 0x595e9fbd94fda766ULL;

EXPORT void __stack_chk_fail(void)
{
    static const char msg[] = "*** stack smashing detected ***: terminated\n";
    if (__k && __k->write)
        __k->write(__k->pid, msg, (int)sizeof(msg) - 1);
    abort();
}

// POSIX errno location accessor (required by glibc and musl)
EXPORT int *__errno_location(void)
{
    return &errno;
}

EXPORT int *__errno(void)
{
    return &errno;
}

// ---- File Descriptor Table ----
#define MAX_FDS 64

typedef struct {
    int   in_use;
    int   is_write;
    int   is_append;
    char  path[260];
    char *buffer;
    int   size;
    int   capacity;
    int   pos;
} PosixFd;

static PosixFd s_fds[MAX_FDS];
static int s_fds_initialized = 0;

static void init_fds(void)
{
    if (s_fds_initialized) return;
    memset(s_fds, 0, sizeof(s_fds));
    // fd 0: stdin
    s_fds[0].in_use = 1;
    // fd 1: stdout
    s_fds[1].in_use = 1;
    s_fds[1].is_write = 1;
    // fd 2: stderr
    s_fds[2].in_use = 1;
    s_fds[2].is_write = 1;
    s_fds_initialized = 1;
}

// ---- POSIX File Descriptor I/O ----

EXPORT int open(const char *pathname, int flags, ...)
{
    init_fds();
    if (!pathname || !__k) { errno = EFAULT; return -1; }

    int fd = -1;
    for (int i = 3; i < MAX_FDS; i++) {
        if (!s_fds[i].in_use) { fd = i; break; }
    }
    if (fd < 0) { errno = EMFILE; return -1; }

    int is_write = (flags & (O_WRONLY | O_RDWR)) != 0;
    int is_creat = (flags & O_CREAT) != 0;
    int is_trunc = (flags & O_TRUNC) != 0;

    char buf[1024];
    int read_len = 0;
    if (!is_trunc) {
        read_len = __k->read_file(__k->pid, pathname, buf, (int)sizeof(buf));
    }

    if (read_len < 0 && !is_creat && !is_write) {
        errno = ENOENT;
        return -1;
    }

    PosixFd *p = &s_fds[fd];
    memset(p, 0, sizeof(PosixFd));
    p->in_use = 1;
    p->is_write = is_write;
    p->is_append = (flags & O_APPEND) != 0;
    strncpy(p->path, pathname, sizeof(p->path) - 1);

    if (read_len > 0) {
        p->capacity = read_len < 4096 ? 4096 : read_len * 2;
        p->buffer = (char *)malloc(p->capacity);
        if (p->buffer) {
            // Read full file if larger than 1024
            if (read_len >= (int)sizeof(buf)) {
                int full = __k->read_file(__k->pid, pathname, p->buffer, p->capacity);
                p->size = full > 0 ? full : 0;
            } else {
                memcpy(p->buffer, buf, read_len);
                p->size = read_len;
            }
        }
    } else {
        p->capacity = 4096;
        p->buffer = (char *)malloc(p->capacity);
        p->size = 0;
    }

    if (p->is_append) {
        p->pos = p->size;
    } else {
        p->pos = 0;
    }

    return fd;
}

EXPORT int open64(const char *pathname, int flags, ...)
{
    return open(pathname, flags);
}

EXPORT int creat(const char *pathname, mode_t mode)
{
    return open(pathname, O_WRONLY | O_CREAT | O_TRUNC, mode);
}

EXPORT ssize_t read(int fd, void *buf, size_t count)
{
    init_fds();
    if (!buf) { errno = EFAULT; return -1; }
    if (fd < 0 || fd >= MAX_FDS || !s_fds[fd].in_use) { errno = EBADF; return -1; }
    if (count == 0) return 0;

    PosixFd *p = &s_fds[fd];
    if (fd == 0) {
        if (__k && __k->read_file) {
            int r = __k->read_file(__k->pid, "stdin", (char *)buf, (int)count);
            if (r < 0) return 0;
            return (ssize_t)r;
        }
        return 0; // EOF
    }

    if (!p->buffer || p->pos >= p->size) return 0; // EOF

    size_t avail = (size_t)(p->size - p->pos);
    size_t to_read = count < avail ? count : avail;
    memcpy(buf, p->buffer + p->pos, to_read);
    p->pos += (int)to_read;
    return (ssize_t)to_read;
}

EXPORT ssize_t write(int fd, const void *buf, size_t count)
{
    init_fds();
    if (!buf) { errno = EFAULT; return -1; }
    if (fd < 0 || fd >= MAX_FDS || !s_fds[fd].in_use) { errno = EBADF; return -1; }
    if (count == 0) return 0;

    if (fd == 1 || fd == 2) {
        // Terminal write
        if (__k && __k->write) {
            __k->write(__k->pid, (const char *)buf, (int)count);
            return (ssize_t)count;
        }
        return (ssize_t)count;
    }

    PosixFd *p = &s_fds[fd];
    if (!p->buffer) {
        p->capacity = (int)count < 4096 ? 4096 : (int)count * 2;
        p->buffer = (char *)malloc(p->capacity);
        p->size = 0;
        p->pos = 0;
    }

    if (p->pos + (int)count > p->capacity) {
        int new_cap = (p->pos + (int)count) * 2;
        char *nb = (char *)realloc(p->buffer, new_cap);
        if (!nb) { errno = ENOMEM; return -1; }
        p->buffer = nb;
        p->capacity = new_cap;
    }

    memcpy(p->buffer + p->pos, buf, count);
    p->pos += (int)count;
    if (p->pos > p->size) p->size = p->pos;

    // Flush to disk if modified
    if (__k && __k->write_file && p->path[0]) {
        __k->write_file(__k->pid, p->path, p->buffer, p->size);
    }

    return (ssize_t)count;
}

EXPORT off_t lseek(int fd, off_t offset, int whence)
{
    init_fds();
    if (fd < 0 || fd >= MAX_FDS || !s_fds[fd].in_use) { errno = EBADF; return -1; }
    PosixFd *p = &s_fds[fd];

    off_t new_pos = p->pos;
    if (whence == SEEK_SET)      new_pos = offset;
    else if (whence == SEEK_CUR) new_pos += offset;
    else if (whence == SEEK_END) new_pos = (off_t)p->size + offset;
    else { errno = EINVAL; return -1; }

    if (new_pos < 0) { errno = EINVAL; return -1; }
    p->pos = (int)new_pos;
    return new_pos;
}

EXPORT off64_t lseek64(int fd, off64_t offset, int whence)
{
    return (off64_t)lseek(fd, (off_t)offset, whence);
}

EXPORT int close(int fd)
{
    init_fds();
    if (fd < 0 || fd >= MAX_FDS || !s_fds[fd].in_use) { errno = EBADF; return -1; }
    if (fd <= 2) return 0; // Don't close standard handles

    PosixFd *p = &s_fds[fd];
    if (p->is_write && p->buffer && __k && __k->write_file && p->path[0]) {
        __k->write_file(__k->pid, p->path, p->buffer, p->size);
    }

    if (p->buffer) {
        free(p->buffer);
        p->buffer = NULL;
    }
    p->in_use = 0;
    return 0;
}

EXPORT int isatty(int fd)
{
    // 0, 1, 2 behave as a tty
    return (fd >= 0 && fd <= 2);
}

EXPORT int ioctl(int fd, unsigned long request, ...)
{
    if (request == TIOCGWINSZ) {
        // Return 80 columns x 25 rows
        struct winsize *ws = NULL;
        __builtin_va_list va;
        __builtin_va_start(va, request);
        ws = __builtin_va_arg(va, struct winsize *);
        __builtin_va_end(va);
        if (ws) {
            ws->ws_col = 80;
            ws->ws_row = 25;
            ws->ws_xpixel = 640;
            ws->ws_ypixel = 480;
            return 0;
        }
    }
    return 0;
}

EXPORT int fcntl(int fd, int cmd, ...)
{
    if (fd < 0 || fd >= MAX_FDS || !s_fds[fd].in_use) { errno = EBADF; return -1; }
    if (cmd == F_GETFL) return s_fds[fd].is_write ? O_RDWR : O_RDONLY;
    if (cmd == F_SETFL) return 0;
    return 0;
}

EXPORT int dup(int oldfd)
{
    if (oldfd < 0 || oldfd >= MAX_FDS || !s_fds[oldfd].in_use) { errno = EBADF; return -1; }
    for (int i = 3; i < MAX_FDS; i++) {
        if (!s_fds[i].in_use) {
            s_fds[i] = s_fds[oldfd];
            return i;
        }
    }
    errno = EMFILE;
    return -1;
}

EXPORT int dup2(int oldfd, int newfd)
{
    if (oldfd < 0 || oldfd >= MAX_FDS || !s_fds[oldfd].in_use) { errno = EBADF; return -1; }
    if (newfd < 0 || newfd >= MAX_FDS) { errno = EBADF; return -1; }
    if (oldfd == newfd) return newfd;
    close(newfd);
    s_fds[newfd] = s_fds[oldfd];
    return newfd;
}

EXPORT int pipe(int pipefd[2])
{
    if (!pipefd) { errno = EFAULT; return -1; }
    pipefd[0] = open("/tmp/pipe_r", O_RDWR | O_CREAT, 0666);
    pipefd[1] = pipefd[0];
    return pipefd[0] >= 0 ? 0 : -1;
}

EXPORT int access(const char *pathname, int mode)
{
    if (!pathname || !__k) { errno = EFAULT; return -1; }
    FileStat st;
    if (__k->stat_file(__k->pid, pathname, &st) == 0) return 0;
    errno = ENOENT;
    return -1;
}

// ---- Filesystem Metadata ----

EXPORT int stat(const char *pathname, struct stat *statbuf)
{
    if (!pathname || !statbuf || !__k) { errno = EFAULT; return -1; }
    memset(statbuf, 0, sizeof(struct stat));

    FileStat kst;
    if (__k->stat_file(__k->pid, pathname, &kst) != 0) {
        errno = ENOENT;
        return -1;
    }

    statbuf->st_mode = kst.is_dir ? (S_IFDIR | 0755) : (S_IFREG | 0644);
    statbuf->st_size = ((off_t)(uint32_t)kst.size_hi << 32) | (uint32_t)kst.size_lo;
    statbuf->st_nlink = 1;
    statbuf->st_blksize = 4096;
    statbuf->st_blocks = (statbuf->st_size + 511) / 512;
    statbuf->st_atime = 1700000000;
    statbuf->st_mtime = 1700000000;
    statbuf->st_ctime = 1700000000;
    return 0;
}

EXPORT int stat64(const char *pathname, void *statbuf)
{
    return stat(pathname, (struct stat *)statbuf);
}

EXPORT int fstat(int fd, struct stat *statbuf)
{
    init_fds();
    if (fd < 0 || fd >= MAX_FDS || !s_fds[fd].in_use) { errno = EBADF; return -1; }
    if (!statbuf) { errno = EFAULT; return -1; }
    memset(statbuf, 0, sizeof(struct stat));

    if (fd <= 2) {
        statbuf->st_mode = S_IFCHR | 0666;
        return 0;
    }

    PosixFd *p = &s_fds[fd];
    statbuf->st_mode = S_IFREG | 0644;
    statbuf->st_size = p->size;
    statbuf->st_nlink = 1;
    statbuf->st_blksize = 4096;
    statbuf->st_blocks = (p->size + 511) / 512;
    return 0;
}

EXPORT int fstat64(int fd, void *statbuf)
{
    return fstat(fd, (struct stat *)statbuf);
}

EXPORT int lstat(const char *pathname, struct stat *statbuf)
{
    return stat(pathname, statbuf);
}

EXPORT int lstat64(const char *pathname, void *statbuf)
{
    return stat(pathname, (struct stat *)statbuf);
}

EXPORT int unlink(const char *pathname)
{
    if (!pathname || !__k) { errno = EFAULT; return -1; }
    if (__k->delete_path(__k->pid, pathname) == 0) return 0;
    errno = ENOENT;
    return -1;
}

EXPORT int rmdir(const char *pathname)
{
    return unlink(pathname);
}

EXPORT int mkdir(const char *pathname, mode_t mode)
{
    if (!pathname || !__k) { errno = EFAULT; return -1; }
    if (__k->make_dir(__k->pid, pathname) == 0) return 0;
    errno = EEXIST;
    return -1;
}

static char s_cwd[260] = "/mnt";

EXPORT char *getcwd(char *buf, size_t size)
{
    if (size < strlen(s_cwd) + 1) { errno = ERANGE; return NULL; }
    char *target = buf ? buf : (char *)malloc(size);
    if (!target) { errno = ENOMEM; return NULL; }
    strcpy(target, s_cwd);
    return target;
}

EXPORT int chdir(const char *path)
{
    if (!path) { errno = EFAULT; return -1; }
    strncpy(s_cwd, path, sizeof(s_cwd) - 1);
    return 0;
}

EXPORT int chmod(const char *pathname, mode_t mode)
{
    return 0;
}

// ---- Directory Enumeration (opendir / readdir / closedir) ----

struct DIR {
    char   path[260];
    char   *names_buffer;
    int    count;
    int    index;
    int    offset;
    struct dirent current;
};

EXPORT DIR *opendir(const char *name)
{
    if (!name || !__k) { errno = EFAULT; return NULL; }
    DIR *d = (DIR *)malloc(sizeof(DIR));
    if (!d) { errno = ENOMEM; return NULL; }
    memset(d, 0, sizeof(DIR));

    strncpy(d->path, name, sizeof(d->path) - 1);
    d->names_buffer = (char *)malloc(8192);
    if (!d->names_buffer) { free(d); errno = ENOMEM; return NULL; }

    d->count = __k->list_dir(__k->pid, name, d->names_buffer, 8192);
    if (d->count < 0) {
        free(d->names_buffer);
        free(d);
        errno = ENOENT;
        return NULL;
    }
    return d;
}

EXPORT struct dirent *readdir(DIR *dirp)
{
    if (!dirp || dirp->index >= dirp->count || !dirp->names_buffer) return NULL;

    const char *name = dirp->names_buffer + dirp->offset;
    dirp->offset += (int)strlen(name) + 1;
    dirp->index++;

    memset(&dirp->current, 0, sizeof(struct dirent));
    dirp->current.d_ino = (ino_t)dirp->index;
    dirp->current.d_type = DT_REG;
    strncpy(dirp->current.d_name, name, sizeof(dirp->current.d_name) - 1);
    return &dirp->current;
}

EXPORT int closedir(DIR *dirp)
{
    if (!dirp) { errno = EBADF; return -1; }
    if (dirp->names_buffer) free(dirp->names_buffer);
    free(dirp);
    return 0;
}

EXPORT void rewinddir(DIR *dirp)
{
    if (dirp) {
        dirp->index = 0;
        dirp->offset = 0;
    }
}

// ---- Memory Mapping (mmap / munmap / mprotect / brk) ----

EXPORT void *mmap(void *addr, size_t length, int prot, int flags, int fd, off_t offset)
{
    if (length == 0) { errno = EINVAL; return MAP_FAILED; }

    void *ptr = malloc(length);
    if (!ptr) { errno = ENOMEM; return MAP_FAILED; }
    memset(ptr, 0, length);

    // If file-backed mmap, read file contents into memory
    if (fd >= 0 && fd < MAX_FDS && s_fds[fd].in_use) {
        PosixFd *p = &s_fds[fd];
        if (p->buffer && offset < p->size) {
            size_t copy_bytes = (size_t)(p->size - offset);
            if (copy_bytes > length) copy_bytes = length;
            memcpy(ptr, p->buffer + offset, copy_bytes);
        }
    }
    return ptr;
}

EXPORT void *mmap64(void *addr, size_t length, int prot, int flags, int fd, off64_t offset)
{
    return mmap(addr, length, prot, flags, fd, (off_t)offset);
}

EXPORT int munmap(void *addr, size_t length)
{
    if (addr && addr != MAP_FAILED) {
        free(addr);
    }
    return 0;
}

EXPORT int mprotect(void *addr, size_t len, int prot)
{
    return 0; // Flat memory architecture
}

static void *s_cur_brk = NULL;

EXPORT int brk(void *addr)
{
    if (!addr) {
        if (!s_cur_brk) s_cur_brk = malloc(64 * 1024);
        return 0;
    }
    s_cur_brk = addr;
    return 0;
}

EXPORT void *sbrk(intptr_t increment)
{
    if (!s_cur_brk) s_cur_brk = malloc(64 * 1024);
    void *old = s_cur_brk;
    s_cur_brk = (void *)((uintptr_t)s_cur_brk + increment);
    return old;
}

// ---- Process & User Identification ----

EXPORT pid_t getpid(void)
{
    return __k ? __k->pid : 1;
}

EXPORT pid_t getppid(void)
{
    return 1;
}

EXPORT uid_t getuid(void) { return 0; }
EXPORT uid_t geteuid(void) { return 0; }
EXPORT gid_t getgid(void) { return 0; }
EXPORT gid_t getegid(void) { return 0; }

EXPORT unsigned int sleep(unsigned int seconds)
{
    if (__k && __k->sleep) __k->sleep(__k->pid, (int)seconds * 1000);
    return 0;
}

EXPORT int usleep(useconds_t usec)
{
    if (__k && __k->sleep) __k->sleep(__k->pid, (int)(usec / 1000));
    return 0;
}

EXPORT int uname(struct utsname *buf)
{
    if (!buf) { errno = EFAULT; return -1; }
    strncpy(buf->sysname, "Windose", sizeof(buf->sysname) - 1);
    strncpy(buf->nodename, "windose", sizeof(buf->nodename) - 1);
    strncpy(buf->release, "1.0.0", sizeof(buf->release) - 1);
    strncpy(buf->version, "Windose 7 Aero Core", sizeof(buf->version) - 1);
    strncpy(buf->machine, "x86_64", sizeof(buf->machine) - 1);
    strncpy(buf->domainname, "local", sizeof(buf->domainname) - 1);
    return 0;
}

// ---- Glibc Program Starter: __libc_start_main ----

EXPORT int __libc_start_main(
    int (*main_fn)(int, char **, char **),
    int argc,
    char **argv,
    int (*init_fn)(int, char **, char **),
    void (*fini_fn)(void),
    void (*rtld_fini_fn)(void),
    void *stack_end)
{
    init_fds();

    // If argc/argv are missing or uninitialized (e.g. from static Linux _start)
    char args_buf[1024];
    char *local_argv[33];
    if (argc <= 0 || !argv) {
        if (__k && __k->get_args) {
            argc = __k->get_args(__k->pid, args_buf, (int)sizeof(args_buf));
            if (argc < 0) argc = 0;
            if (argc > 32) argc = 32;
            char *p = args_buf;
            for (int i = 0; i < argc; i++) {
                local_argv[i] = p;
                p += strlen(p) + 1;
            }
            local_argv[argc] = NULL;
            argv = local_argv;
        }
    }

    if (init_fn) {
        init_fn(argc, argv, environ);
    }

    int ret = 0;
    if (main_fn) {
        ret = main_fn(argc, argv, environ);
    }

    if (fini_fn) {
        fini_fn();
    }

    exit(ret);
    return ret;
}

// ---- C++ Runtime Destructor Hooks ----

EXPORT int __cxa_atexit(void (*func)(void *), void *arg, void *dso_handle)
{
    // Register as atexit handler
    return atexit((void (*)(void))func);
}

EXPORT void __cxa_finalize(void *dso_handle)
{
    // Clean up on exit
}

// ---- Dynamic Linker Interfaces (dlopen / dlsym / dlclose / dlerror) ----

EXPORT void *dlopen(const char *filename, int flags)
{
    // Windose returns 0x1 as pseudo-handle for main/default DSO
    return (void *)0x1;
}

EXPORT void *dlsym(void *handle, const char *symbol)
{
    if (!symbol) return NULL;
    // Map standard libc symbols
    if (strcmp(symbol, "printf") == 0) return (void *)printf;
    if (strcmp(symbol, "malloc") == 0) return (void *)malloc;
    if (strcmp(symbol, "free") == 0)   return (void *)free;
    if (strcmp(symbol, "exit") == 0)   return (void *)exit;
    if (strcmp(symbol, "open") == 0)   return (void *)open;
    if (strcmp(symbol, "read") == 0)   return (void *)read;
    if (strcmp(symbol, "write") == 0)  return (void *)write;
    if (strcmp(symbol, "close") == 0)  return (void *)close;
    return NULL;
}

EXPORT int dlclose(void *handle)
{
    return 0;
}

EXPORT char *dlerror(void)
{
    return NULL;
}
