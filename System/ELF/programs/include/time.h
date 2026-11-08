#ifndef _TIME_H
#define _TIME_H

#include <stddef.h>

typedef long long time_t;
typedef long clock_t;

#define CLOCKS_PER_SEC 1000

struct tm {
    int tm_sec;   /* seconds [0, 60] */
    int tm_min;   /* minutes [0, 59] */
    int tm_hour;  /* hours [0, 23] */
    int tm_mday;  /* day of month [1, 31] */
    int tm_mon;   /* month of year [0, 11] */
    int tm_year;  /* years since 1900 */
    int tm_wday;  /* day of week [0, 6] (Sunday = 0) */
    int tm_yday;  /* day of year [0, 365] */
    int tm_isdst; /* daylight saving time flag */
};

clock_t clock(void);
time_t time(time_t *tloc);
double difftime(time_t time1, time_t time0);

struct tm *gmtime(const time_t *timer);
struct tm *gmtime_r(const time_t *timer, struct tm *result);
struct tm *localtime(const time_t *timer);
struct tm *localtime_r(const time_t *timer, struct tm *result);
time_t mktime(struct tm *timeptr);

char *asctime(const struct tm *timeptr);
char *asctime_r(const struct tm *timeptr, char *buf);
char *ctime(const time_t *timer);
char *ctime_r(const time_t *timer, char *buf);

size_t strftime(char *s, size_t max, const char *format, const struct tm *tm);

#endif
