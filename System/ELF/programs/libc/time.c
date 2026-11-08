#include <time.h>
#include <stdio.h>
#include <string.h>
#include "kernel.h"

static const char *const days_abbr[] = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
static const char *const days_full[] = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
static const char *const months_abbr[] = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
static const char *const months_full[] = {
    "January", "February", "March", "April", "May", "June",
    "July", "August", "September", "October", "November", "December"
};
static const int days_per_month[2][12] = {
    { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 },
    { 31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 }
};

static int is_leap(int year)
{
    return (year % 4 == 0 && year % 100 != 0) || (year % 400 == 0);
}

clock_t clock(void)
{
    return (clock_t)k_ticks();
}

time_t time(time_t *tloc)
{
    // Epoch base: Jan 1 2026 00:00:00 UTC (1767225600) + ticks/1000
    time_t t = 1767225600LL + (time_t)(k_ticks() / 1000);
    if (tloc) *tloc = t;
    return t;
}

double difftime(time_t time1, time_t time0)
{
    return (double)(time1 - time0);
}

struct tm *gmtime_r(const time_t *timer, struct tm *res)
{
    if (!timer || !res) return NULL;
    time_t t = *timer;
    if (t < 0) t = 0;

    time_t day_secs = t % 86400LL;
    time_t days = t / 86400LL;

    res->tm_sec = (int)(day_secs % 60LL);
    day_secs /= 60LL;
    res->tm_min = (int)(day_secs % 60LL);
    res->tm_hour = (int)(day_secs / 60LL);

    res->tm_wday = (int)((days + 4LL) % 7LL);
    if (res->tm_wday < 0) res->tm_wday += 7;

    int year = 1970;
    while (1) {
        int leap = is_leap(year);
        int ydays = leap ? 366 : 365;
        if (days < ydays) break;
        days -= ydays;
        year++;
    }

    res->tm_year = year - 1900;
    res->tm_yday = (int)days;

    int leap = is_leap(year);
    int mon = 0;
    while (mon < 12 && days >= days_per_month[leap][mon]) {
        days -= days_per_month[leap][mon];
        mon++;
    }
    res->tm_mon = mon;
    res->tm_mday = (int)days + 1;
    res->tm_isdst = 0;

    return res;
}

static struct tm static_tm;
struct tm *gmtime(const time_t *timer)
{
    return gmtime_r(timer, &static_tm);
}

struct tm *localtime_r(const time_t *timer, struct tm *res)
{
    return gmtime_r(timer, res);
}

struct tm *localtime(const time_t *timer)
{
    return gmtime_r(timer, &static_tm);
}

time_t mktime(struct tm *tm)
{
    if (!tm) return -1;
    int year = tm->tm_year + 1900;
    time_t days = 0;

    for (int y = 1970; y < year; y++) {
        days += is_leap(y) ? 366 : 365;
    }

    int leap = is_leap(year);
    for (int m = 0; m < tm->tm_mon && m < 12; m++) {
        days += days_per_month[leap][m];
    }
    days += (tm->tm_mday - 1);

    time_t secs = days * 86400LL + (time_t)tm->tm_hour * 3600LL + (time_t)tm->tm_min * 60LL + (time_t)tm->tm_sec;
    return secs;
}

char *asctime_r(const struct tm *tm, char *buf)
{
    if (!tm || !buf) return NULL;
    int wday = (tm->tm_wday >= 0 && tm->tm_wday < 7) ? tm->tm_wday : 0;
    int mon = (tm->tm_mon >= 0 && tm->tm_mon < 12) ? tm->tm_mon : 0;
    snprintf(buf, 26, "%.3s %.3s%3d %.2d:%.2d:%.2d %d\n",
             days_abbr[wday], months_abbr[mon],
             tm->tm_mday, tm->tm_hour, tm->tm_min, tm->tm_sec,
             1900 + tm->tm_year);
    return buf;
}

static char static_asc_buf[32];
char *asctime(const struct tm *tm)
{
    return asctime_r(tm, static_asc_buf);
}

char *ctime_r(const time_t *timer, char *buf)
{
    struct tm tm;
    if (!localtime_r(timer, &tm)) return NULL;
    return asctime_r(&tm, buf);
}

char *ctime(const time_t *timer)
{
    return ctime_r(timer, static_asc_buf);
}

size_t strftime(char *s, size_t max, const char *format, const struct tm *tm)
{
    if (!s || max == 0 || !format || !tm) return 0;
    char *dest = s;
    size_t left = max;

    while (*format && left > 1) {
        if (*format != '%') {
            *dest++ = *format++;
            left--;
            continue;
        }
        format++;
        char tmp[64];
        tmp[0] = '\0';

        switch (*format) {
        case '%': snprintf(tmp, sizeof(tmp), "%%"); break;
        case 'Y': snprintf(tmp, sizeof(tmp), "%04d", 1900 + tm->tm_year); break;
        case 'y': snprintf(tmp, sizeof(tmp), "%02d", (1900 + tm->tm_year) % 100); break;
        case 'm': snprintf(tmp, sizeof(tmp), "%02d", tm->tm_mon + 1); break;
        case 'd': snprintf(tmp, sizeof(tmp), "%02d", tm->tm_mday); break;
        case 'e': snprintf(tmp, sizeof(tmp), "%2d", tm->tm_mday); break;
        case 'H': snprintf(tmp, sizeof(tmp), "%02d", tm->tm_hour); break;
        case 'I': {
            int h = tm->tm_hour % 12;
            if (h == 0) h = 12;
            snprintf(tmp, sizeof(tmp), "%02d", h);
            break;
        }
        case 'p': snprintf(tmp, sizeof(tmp), tm->tm_hour >= 12 ? "PM" : "AM"); break;
        case 'M': snprintf(tmp, sizeof(tmp), "%02d", tm->tm_min); break;
        case 'S': snprintf(tmp, sizeof(tmp), "%02d", tm->tm_sec); break;
        case 'a': snprintf(tmp, sizeof(tmp), "%s", days_abbr[(tm->tm_wday >= 0 && tm->tm_wday < 7) ? tm->tm_wday : 0]); break;
        case 'A': snprintf(tmp, sizeof(tmp), "%s", days_full[(tm->tm_wday >= 0 && tm->tm_wday < 7) ? tm->tm_wday : 0]); break;
        case 'b': case 'h': snprintf(tmp, sizeof(tmp), "%s", months_abbr[(tm->tm_mon >= 0 && tm->tm_mon < 12) ? tm->tm_mon : 0]); break;
        case 'B': snprintf(tmp, sizeof(tmp), "%s", months_full[(tm->tm_mon >= 0 && tm->tm_mon < 12) ? tm->tm_mon : 0]); break;
        case 'w': snprintf(tmp, sizeof(tmp), "%d", tm->tm_wday); break;
        case 'j': snprintf(tmp, sizeof(tmp), "%03d", tm->tm_yday + 1); break;
        case 'c': asctime_r(tm, tmp); if (tmp[strlen(tmp)-1] == '\n') tmp[strlen(tmp)-1] = '\0'; break;
        case 'x': snprintf(tmp, sizeof(tmp), "%02d/%02d/%02d", tm->tm_mon + 1, tm->tm_mday, (1900 + tm->tm_year) % 100); break;
        case 'X': snprintf(tmp, sizeof(tmp), "%02d:%02d:%02d", tm->tm_hour, tm->tm_min, tm->tm_sec); break;
        default:
            tmp[0] = '%';
            tmp[1] = *format;
            tmp[2] = '\0';
            break;
        }
        format++;

        size_t len = strlen(tmp);
        if (len >= left) len = left - 1;
        memcpy(dest, tmp, len);
        dest += len;
        left -= len;
    }
    *dest = '\0';
    return (size_t)(dest - s);
}
