#ifndef WINDOSE_STDIO_H
#define WINDOSE_STDIO_H

#include <stddef.h>
#include <stdarg.h>

#define EOF (-1)

#define SEEK_SET 0
#define SEEK_CUR 1
#define SEEK_END 2

#define _IOFBF 0
#define _IOLBF 1
#define _IONBF 2

#define BUFSIZ 1024
#define FOPEN_MAX 16
#define FILENAME_MAX 260
#define TMP_MAX 32767
#define L_tmpnam 260

typedef long fpos_t;

typedef struct _IO_FILE {
    int fd;             /* -1 for file, 0 stdin, 1 stdout, 2 stderr */
    int flags;          /* open mode flags */
    char path[260];
    char *buffer;
    size_t buf_size;
    size_t buf_pos;
    size_t buf_len;
    int unget;
    int eof;
    int error;
    int is_dirty;
    int is_alloc;
    int is_static;
} FILE;

extern FILE *stdin;
extern FILE *stdout;
extern FILE *stderr;

/* File access */
FILE *fopen(const char *pathname, const char *mode);
FILE *freopen(const char *pathname, const char *mode, FILE *stream);
int fclose(FILE *stream);
int fflush(FILE *stream);
void setbuf(FILE *stream, char *buf);
int setvbuf(FILE *stream, char *buf, int mode, size_t size);

/* File system operations */
int remove(const char *pathname);
int rename(const char *oldpath, const char *newpath);

/* Direct I/O */
size_t fread(void *ptr, size_t size, size_t nmemb, FILE *stream);
size_t fwrite(const void *ptr, size_t size, size_t nmemb, FILE *stream);

/* File positioning */
int fseek(FILE *stream, long offset, int whence);
long ftell(FILE *stream);
void rewind(FILE *stream);
int fgetpos(FILE *stream, fpos_t *pos);
int fsetpos(FILE *stream, const fpos_t *pos);

/* Error handling */
int feof(FILE *stream);
int ferror(FILE *stream);
void clearerr(FILE *stream);
void perror(const char *s);

/* Character and line I/O */
int fgetc(FILE *stream);
int getc(FILE *stream);
int getchar(void);
char *fgets(char *s, int size, FILE *stream);

int fputc(int c, FILE *stream);
int putc(int c, FILE *stream);
int putchar(int c);
int fputs(const char *s, FILE *stream);
int puts(const char *s);
int ungetc(int c, FILE *stream);

/* Formatted output */
int printf(const char *fmt, ...) __attribute__((format(printf, 1, 2)));
int fprintf(FILE *stream, const char *fmt, ...) __attribute__((format(printf, 2, 3)));
int sprintf(char *buf, const char *fmt, ...) __attribute__((format(printf, 2, 3)));
int snprintf(char *buf, size_t n, const char *fmt, ...) __attribute__((format(printf, 3, 4)));

int vprintf(const char *fmt, va_list ap);
int vfprintf(FILE *stream, const char *fmt, va_list ap);
int vsprintf(char *buf, const char *fmt, va_list ap);
int vsnprintf(char *buf, size_t n, const char *fmt, va_list ap);

/* Formatted input */
int scanf(const char *fmt, ...);
int fscanf(FILE *stream, const char *fmt, ...);
int sscanf(const char *s, const char *fmt, ...);
int vscanf(const char *fmt, va_list ap);
int vfscanf(FILE *stream, const char *fmt, va_list ap);
int vsscanf(const char *s, const char *fmt, va_list ap);

/* Whole-file helper (max 1 MB). Returns a malloc'd buffer (NUL-terminated) or NULL; *size = byte count. */
char *file_read(const char *path, int *size);

#endif
