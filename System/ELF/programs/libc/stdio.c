#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include "kernel.h"

int __vformat(char *buf, size_t cap, const char *fmt, va_list ap);

static FILE __stdin_file  = { .fd = 0, .unget = -1, .is_static = 1 };
static FILE __stdout_file = { .fd = 1, .unget = -1, .is_static = 1 };
static FILE __stderr_file = { .fd = 2, .unget = -1, .is_static = 1 };

FILE *stdin  = &__stdin_file;
FILE *stdout = &__stdout_file;
FILE *stderr = &__stderr_file;

#define MAX_OPEN_FILES 16
static FILE *open_files[MAX_OPEN_FILES] = { &__stdin_file, &__stdout_file, &__stderr_file };

void __stdio_cleanup(void)
{
    for (int i = 0; i < MAX_OPEN_FILES; i++) {
        if (open_files[i]) {
            fflush(open_files[i]);
            if (!open_files[i]->is_static) {
                if (open_files[i]->is_alloc && open_files[i]->buffer) {
                    free(open_files[i]->buffer);
                    open_files[i]->buffer = NULL;
                }
                free(open_files[i]);
                open_files[i] = NULL;
            }
        }
    }
}

static int register_file(FILE *f)
{
    for (int i = 0; i < MAX_OPEN_FILES; i++) {
        if (!open_files[i]) {
            open_files[i] = f;
            return 0;
        }
    }
    return -1;
}

static void unregister_file(FILE *f)
{
    for (int i = 0; i < MAX_OPEN_FILES; i++) {
        if (open_files[i] == f) {
            open_files[i] = NULL;
            return;
        }
    }
}

static int ensure_capacity(FILE *stream, size_t needed)
{
    if (needed <= stream->buf_size) return 1;
    size_t new_size = stream->buf_size ? stream->buf_size * 2 : 1024;
    while (new_size < needed) new_size *= 2;
    char *new_buf = malloc(new_size);
    if (!new_buf) return 0;
    if (stream->buffer && stream->buf_len > 0) {
        memcpy(new_buf, stream->buffer, stream->buf_len);
    }
    if (stream->is_alloc && stream->buffer) {
        free(stream->buffer);
    }
    stream->buffer = new_buf;
    stream->buf_size = new_size;
    stream->is_alloc = 1;
    return 1;
}

FILE *fopen(const char *pathname, const char *mode)
{
    if (!pathname || !mode) {
        errno = EINVAL;
        return NULL;
    }

    int is_read = (strchr(mode, 'r') != NULL);
    int is_write = (strchr(mode, 'w') != NULL);
    int is_append = (strchr(mode, 'a') != NULL);
    int is_plus = (strchr(mode, '+') != NULL);

    if (!is_read && !is_write && !is_append) {
        errno = EINVAL;
        return NULL;
    }

    FileStat st;
    int exists = (k_stat_file(pathname, &st) == 0);

    if (is_read && !is_plus && !exists) {
        errno = ENOENT;
        return NULL;
    }

    FILE *f = calloc(1, sizeof(FILE));
    if (!f) {
        errno = ENOMEM;
        return NULL;
    }

    f->fd = -1;
    f->unget = -1;
    strncpy(f->path, pathname, sizeof(f->path) - 1);

    if (exists && !is_write) {
        int initial_cap = st.size_lo > 0 ? st.size_lo : 1024;
        f->buffer = malloc((size_t)initial_cap + 1);
        if (!f->buffer) {
            free(f);
            errno = ENOMEM;
            return NULL;
        }
        f->buf_size = (size_t)initial_cap;
        f->is_alloc = 1;
        int n = k_read_file(pathname, f->buffer, initial_cap);
        if (n >= 0) {
            f->buf_len = (size_t)n;
            f->buffer[n] = '\0';
        } else {
            f->buf_len = 0;
        }
    } else {
        f->buf_size = 1024;
        f->buffer = malloc(f->buf_size);
        if (f->buffer) {
            f->is_alloc = 1;
            f->buffer[0] = '\0';
        }
        f->buf_len = 0;
    }

    if (is_append) {
        f->buf_pos = f->buf_len;
    } else {
        f->buf_pos = 0;
    }

    if (register_file(f) < 0) {
        if (f->is_alloc && f->buffer) free(f->buffer);
        free(f);
        errno = EMFILE;
        return NULL;
    }

    return f;
}

FILE *freopen(const char *pathname, const char *mode, FILE *stream)
{
    if (stream) fclose(stream);
    return fopen(pathname, mode);
}

int fflush(FILE *stream)
{
    if (!stream) {
        for (int i = 0; i < MAX_OPEN_FILES; i++) {
            if (open_files[i]) fflush(open_files[i]);
        }
        return 0;
    }

    if (stream->is_dirty && stream->fd == -1 && stream->path[0]) {
        int r = k_write_file(stream->path, stream->buffer, (int)stream->buf_len);
        if (r != 0) {
            stream->error = 1;
            return EOF;
        }
        stream->is_dirty = 0;
    }
    return 0;
}

int fclose(FILE *stream)
{
    if (!stream) return EOF;
    fflush(stream);
    unregister_file(stream);
    if (!stream->is_static) {
        if (stream->is_alloc && stream->buffer) {
            free(stream->buffer);
            stream->buffer = NULL;
        }
        free(stream);
    }
    return 0;
}

void setbuf(FILE *stream, char *buf)
{
    setvbuf(stream, buf, buf ? _IOFBF : _IONBF, BUFSIZ);
}

int setvbuf(FILE *stream, char *buf, int mode, size_t size)
{
    if (!stream) return -1;
    (void)mode;
    if (buf && size > 0) {
        if (stream->is_alloc && stream->buffer) free(stream->buffer);
        stream->buffer = buf;
        stream->buf_size = size;
        stream->is_alloc = 0;
    }
    return 0;
}

int remove(const char *pathname)
{
    if (!pathname) { errno = EINVAL; return -1; }
    int r = k_delete_path(pathname);
    if (r != 0) { errno = ENOENT; return -1; }
    return 0;
}

int rename(const char *oldpath, const char *newpath)
{
    if (!oldpath || !newpath) { errno = EINVAL; return -1; }
    int r = k_rename_path(oldpath, newpath);
    if (r != 0) { errno = EACCES; return -1; }
    return 0;
}

size_t fwrite(const void *ptr, size_t size, size_t nmemb, FILE *stream)
{
    if (!stream || size == 0 || nmemb == 0) return 0;
    size_t total = size * nmemb;
    if (stream->fd == 1 || stream->fd == 2) {
        k_write((const char *)ptr, (int)total);
        return nmemb;
    }
    if (!ensure_capacity(stream, stream->buf_pos + total + 1)) {
        stream->error = 1;
        return 0;
    }
    memcpy(stream->buffer + stream->buf_pos, ptr, total);
    stream->buf_pos += total;
    if (stream->buf_pos > stream->buf_len) {
        stream->buf_len = stream->buf_pos;
    }
    stream->buffer[stream->buf_len] = '\0';
    stream->is_dirty = 1;
    return nmemb;
}

size_t fread(void *ptr, size_t size, size_t nmemb, FILE *stream)
{
    if (!stream || size == 0 || nmemb == 0) return 0;
    size_t total = size * nmemb;
    char *dest = (char *)ptr;
    size_t read_bytes = 0;

    if (stream->unget != -1) {
        *dest++ = (char)stream->unget;
        stream->unget = -1;
        read_bytes++;
    }

    if (stream->fd == 0) {
        while (read_bytes < total) {
            int c = fgetc(stream);
            if (c == EOF) break;
            *dest++ = (char)c;
            read_bytes++;
        }
        return read_bytes / size;
    }

    size_t avail = (stream->buf_len > stream->buf_pos) ? (stream->buf_len - stream->buf_pos) : 0;
    size_t to_copy = total - read_bytes;
    if (to_copy > avail) to_copy = avail;

    if (to_copy > 0) {
        memcpy(dest, stream->buffer + stream->buf_pos, to_copy);
        stream->buf_pos += to_copy;
        read_bytes += to_copy;
    }

    if (stream->buf_pos >= stream->buf_len) {
        stream->eof = 1;
    }
    return read_bytes / size;
}

int fseek(FILE *stream, long offset, int whence)
{
    if (!stream) return -1;
    long new_pos = 0;
    if (whence == SEEK_SET) new_pos = offset;
    else if (whence == SEEK_CUR) new_pos = (long)stream->buf_pos + offset;
    else if (whence == SEEK_END) new_pos = (long)stream->buf_len + offset;
    else return -1;

    if (new_pos < 0) return -1;
    if ((size_t)new_pos > stream->buf_len) {
        if (!ensure_capacity(stream, (size_t)new_pos + 1)) return -1;
        memset(stream->buffer + stream->buf_len, 0, (size_t)new_pos - stream->buf_len);
        stream->buf_len = (size_t)new_pos;
        stream->buffer[stream->buf_len] = '\0';
    }
    stream->buf_pos = (size_t)new_pos;
    stream->eof = 0;
    return 0;
}

long ftell(FILE *stream)
{
    if (!stream) return -1;
    return (long)stream->buf_pos;
}

void rewind(FILE *stream)
{
    if (stream) {
        fseek(stream, 0, SEEK_SET);
        stream->error = 0;
    }
}

int fgetpos(FILE *stream, fpos_t *pos)
{
    if (!stream || !pos) return -1;
    *pos = ftell(stream);
    return 0;
}

int fsetpos(FILE *stream, const fpos_t *pos)
{
    if (!stream || !pos) return -1;
    return fseek(stream, *pos, SEEK_SET);
}

int feof(FILE *stream) { return stream ? stream->eof : 1; }
int ferror(FILE *stream) { return stream ? stream->error : 1; }
void clearerr(FILE *stream) { if (stream) { stream->error = 0; stream->eof = 0; } }

void perror(const char *s)
{
    if (s && *s) {
        fprintf(stderr, "%s: error %d\n", s, errno);
    } else {
        fprintf(stderr, "error %d\n", errno);
    }
}

int fgetc(FILE *stream)
{
    if (!stream) return EOF;
    if (stream->unget != -1) {
        int c = stream->unget;
        stream->unget = -1;
        return c;
    }
    if (stream->fd == 0) {
        if (__k && __k->read_file) {
            char ch = 0;
            int r = __k->read_file(__k->pid, "stdin", &ch, 1);
            if (r <= 0) return EOF;
            return (unsigned char)ch;
        }
        return EOF;
    }
    if (stream->buf_pos >= stream->buf_len) {
        stream->eof = 1;
        return EOF;
    }
    return (unsigned char)stream->buffer[stream->buf_pos++];
}

int getc(FILE *stream) { return fgetc(stream); }
int getchar(void) { return fgetc(stdin); }

int ungetc(int c, FILE *stream)
{
    if (!stream || c == EOF) return EOF;
    stream->unget = (unsigned char)c;
    stream->eof = 0;
    return c;
}

int fputc(int c, FILE *stream)
{
    unsigned char ch = (unsigned char)c;
    return fwrite(&ch, 1, 1, stream) == 1 ? ch : EOF;
}

int putc(int c, FILE *stream) { return fputc(c, stream); }
int putchar(int c) { return fputc(c, stdout); }

char *fgets(char *s, int size, FILE *stream)
{
    if (!s || size <= 0 || !stream) return NULL;
    int i = 0;
    while (i < size - 1) {
        int c = fgetc(stream);
        if (c == EOF) {
            if (i == 0) return NULL;
            break;
        }
        s[i++] = (char)c;
        if (c == '\n') break;
    }
    s[i] = '\0';
    return s;
}

int fputs(const char *s, FILE *stream)
{
    if (!s || !stream) return EOF;
    size_t len = strlen(s);
    return fwrite(s, 1, len, stream) == len ? 0 : EOF;
}

int puts(const char *s)
{
    if (!s) return EOF;
    fputs(s, stdout);
    putchar('\n');
    return 1;
}

int vfprintf(FILE *stream, const char *fmt, va_list ap)
{
    if (!stream) return -1;
    char small[512];
    va_list ap2;
    va_copy(ap2, ap);
    int len = __vformat(small, sizeof small, fmt, ap);
    if (len < (int)sizeof small) {
        va_end(ap2);
        fwrite(small, 1, (size_t)len, stream);
        return len;
    }
    char *big = malloc((size_t)len + 1);
    if (!big) {
        va_end(ap2);
        fwrite(small, 1, sizeof small - 1, stream);
        return len;
    }
    __vformat(big, (size_t)len + 1, fmt, ap2);
    va_end(ap2);
    fwrite(big, 1, (size_t)len, stream);
    free(big);
    return len;
}

int fprintf(FILE *stream, const char *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    int r = vfprintf(stream, fmt, ap);
    va_end(ap);
    return r;
}

int vprintf(const char *fmt, va_list ap)
{
    return vfprintf(stdout, fmt, ap);
}

int printf(const char *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    int r = vfprintf(stdout, fmt, ap);
    va_end(ap);
    return r;
}

int vsnprintf(char *buf, size_t n, const char *fmt, va_list ap) { return __vformat(buf, n, fmt, ap); }
int vsprintf(char *buf, const char *fmt, va_list ap) { return __vformat(buf, (size_t)-1 >> 1, fmt, ap); }

int snprintf(char *buf, size_t n, const char *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    int r = __vformat(buf, n, fmt, ap);
    va_end(ap);
    return r;
}

int sprintf(char *buf, const char *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    int r = __vformat(buf, (size_t)-1 >> 1, fmt, ap);
    va_end(ap);
    return r;
}

int vfscanf(FILE *stream, const char *fmt, va_list ap)
{
    if (!stream) return -1;
    char line[1024];
    if (!fgets(line, sizeof(line), stream)) return -1;
    return vsscanf(line, fmt, ap);
}

int fscanf(FILE *stream, const char *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    int r = vfscanf(stream, fmt, ap);
    va_end(ap);
    return r;
}

int vscanf(const char *fmt, va_list ap)
{
    return vfscanf(stdin, fmt, ap);
}

int scanf(const char *fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    int r = vfscanf(stdin, fmt, ap);
    va_end(ap);
    return r;
}

char *file_read(const char *path, int *size)
{
    int cap = 64 * 1024;
    for (;;) {
        char *buf = malloc((size_t)cap + 1);
        if (!buf) return NULL;
        int n = k_read_file(path, buf, cap);
        if (n < 0) { free(buf); return NULL; }
        if (n < cap || cap >= 1024 * 1024) { buf[n] = 0; if (size) *size = n; return buf; }
        free(buf);
        cap *= 2;
    }
}
