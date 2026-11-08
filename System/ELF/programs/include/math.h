#ifndef _MATH_H
#define _MATH_H

#define M_E         2.71828182845904523536
#define M_LOG2E     1.44269504088896340736
#define M_LOG10E    0.43429448190325182765
#define M_LN2       0.69314718055994530942
#define M_LN10      2.30258509299404568402
#define M_PI        3.14159265358979323846
#define M_PI_2      1.57079632679489661923
#define M_PI_4      0.78539816339744830962
#define M_1_PI      0.31830988618379067154
#define M_2_PI      0.63661977236758134308
#define M_2_SQRTPI  1.12837916709551257390
#define M_SQRT2     1.41421356237309504880
#define M_SQRT1_2   0.70710678118654752440

#if defined(__GNUC__) || defined(__clang__)
#define INFINITY    (__builtin_inff())
#define NAN         (__builtin_nanf(""))
#define HUGE_VAL    (__builtin_huge_val())
#define HUGE_VALF   (__builtin_huge_valf())
#define HUGE_VALL   (__builtin_huge_vall())

#define isnan(x)    __builtin_isnan(x)
#define isinf(x)    __builtin_isinf(x)
#define isfinite(x) __builtin_isfinite(x)
#define signbit(x)  __builtin_signbit(x)
#else
#define INFINITY    ((float)(1e+300 * 1e+300))
#define NAN         ((float)(INFINITY * 0.0f))
#define HUGE_VAL    ((double)INFINITY)
#define HUGE_VALF   ((float)INFINITY)
#define HUGE_VALL   ((long double)INFINITY)

#define isnan(x)    ((x) != (x))
#define isinf(x)    (!isnan(x) && isnan((x) - (x)))
#define isfinite(x) (!isnan(x) && !isinf(x))
#define signbit(x)  (((char *)&(x))[sizeof(x)-1] & 0x80)
#endif

/* Basic & rounding */
double fabs(double x);
float fabsf(float x);
long double fabsl(long double x);

double floor(double x);
float floorf(float x);

double ceil(double x);
float ceilf(float x);

double round(double x);
float roundf(float x);

double trunc(double x);
float truncf(float x);

double fmod(double x, double y);
float fmodf(float x, float y);

double remainder(double x, double y);

double fmin(double x, double y);
float fminf(float x, float y);

double fmax(double x, double y);
float fmaxf(float x, float y);

double fdim(double x, double y);

double copysign(double x, double y);
float copysignf(float x, float y);

/* Powers, roots & exponentials */
double sqrt(double x);
float sqrtf(float x);

double cbrt(double x);
double hypot(double x, double y);

double exp(double x);
float expf(float x);
double exp2(double x);

double log(double x);
float logf(float x);
double log10(double x);
float log10f(float x);
double log2(double x);

double pow(double x, double y);
float powf(float x, float y);

double ldexp(double x, int exp);
double frexp(double x, int *exp);
double modf(double x, double *iptr);

/* Trigonometric functions */
double sin(double x);
float sinf(float x);

double cos(double x);
float cosf(float x);

double tan(double x);
float tanf(float x);

double asin(double x);
float asinf(float x);

double acos(double x);
float acosf(float x);

double atan(double x);
float atanf(float x);

double atan2(double y, double x);
float atan2f(float y, float x);

/* Hyperbolic functions */
double sinh(double x);
double cosh(double x);
double tanh(double x);

#endif
