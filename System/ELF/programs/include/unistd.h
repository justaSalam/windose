#ifndef _UNISTD_H
#define _UNISTD_H

#include <sys/types.h>
#include <stddef.h>

#define STDIN_FILENO  0
#define STDOUT_FILENO 1
#define STDERR_FILENO 2

#define SEEK_SET 0
#define SEEK_CUR 1
#define SEEK_END 2

#define F_OK 0
#define X_OK 1
#define W_OK 2
#define R_OK 4

extern char **environ;

ssize_t read(int fd, void *buf, size_t count);
ssize_t write(int fd, const void *buf, size_t count);
int     close(int fd);
off_t   lseek(int fd, off_t offset, int whence);
off64_t lseek64(int fd, off64_t offset, int whence);
int     unlink(const char *pathname);
int     rmdir(const char *pathname);
int     chdir(const char *path);
char *  getcwd(char *buf, size_t size);
int     access(const char *pathname, int mode);
int     isatty(int fd);
int     dup(int oldfd);
int     dup2(int oldfd, int newfd);
int     pipe(int pipefd[2]);

pid_t   getpid(void);
pid_t   getppid(void);
uid_t   getuid(void);
uid_t   geteuid(void);
gid_t   getgid(void);
gid_t   getegid(void);

unsigned int sleep(unsigned int seconds);
int          usleep(useconds_t usec);
void *       sbrk(intptr_t increment);
int          brk(void *addr);

#endif // _UNISTD_H
