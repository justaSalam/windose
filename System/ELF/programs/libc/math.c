#include <math.h>
#include <errno.h>
#include <stdint.h>

/* IEEE-754 double representation */
typedef union {
    double d;
    uint64_t u;
    struct {
        uint32_t lo;
        uint32_t hi;
    } words;
} ieee_double_t;

/* Basic arithmetic and manipulation */

double fabs(double x) {
    ieee_double_t u;
    u.d = x;
    u.u &= ~0x8000000000000000ULL;
    return u.d;
}

float fabsf(float x) {
    return (float)fabs((double)x);
}

long double fabsl(long double x) {
    return fabs((double)x);
}

double copysign(double x, double y) {
    ieee_double_t ux, uy;
    ux.d = x;
    uy.d = y;
    ux.u = (ux.u & ~0x8000000000000000ULL) | (uy.u & 0x8000000000000000ULL);
    return ux.d;
}

float copysignf(float x, float y) {
    return (float)copysign((double)x, (double)y);
}

double floor(double x) {
    if (isnan(x) || isinf(x) || x == 0.0) return x;
    int64_t i = (int64_t)x;
    double d = (double)i;
    if (x < 0.0 && x != d) {
        return d - 1.0;
    }
    return d;
}

float floorf(float x) {
    return (float)floor((double)x);
}

double ceil(double x) {
    if (isnan(x) || isinf(x) || x == 0.0) return x;
    int64_t i = (int64_t)x;
    double d = (double)i;
    if (x > 0.0 && x != d) {
        return d + 1.0;
    }
    return d;
}

float ceilf(float x) {
    return (float)ceil((double)x);
}

double trunc(double x) {
    if (isnan(x) || isinf(x) || x == 0.0) return x;
    return (double)((int64_t)x);
}

float truncf(float x) {
    return (float)trunc((double)x);
}

double round(double x) {
    if (isnan(x) || isinf(x) || x == 0.0) return x;
    if (x >= 0.0) {
        return floor(x + 0.5);
    } else {
        return ceil(x - 0.5);
    }
}

float roundf(float x) {
    return (float)round((double)x);
}

double fmod(double x, double y) {
    if (isnan(x) || isnan(y) || isinf(x) || y == 0.0) {
        errno = EDOM;
        return NAN;
    }
    if (isinf(y)) return x;
    int64_t q = (int64_t)(x / y);
    return x - (double)q * y;
}

float fmodf(float x, float y) {
    return (float)fmod((double)x, (double)y);
}

double remainder(double x, double y) {
    if (isnan(x) || isnan(y) || isinf(x) || y == 0.0) {
        errno = EDOM;
        return NAN;
    }
    double q = round(x / y);
    return x - q * y;
}

double fmin(double x, double y) {
    if (isnan(x)) return y;
    if (isnan(y)) return x;
    return (x < y) ? x : y;
}

float fminf(float x, float y) {
    return (float)fmin((double)x, (double)y);
}

double fmax(double x, double y) {
    if (isnan(x)) return y;
    if (isnan(y)) return x;
    return (x > y) ? x : y;
}

float fmaxf(float x, float y) {
    return (float)fmax((double)x, (double)y);
}

double fdim(double x, double y) {
    if (isnan(x) || isnan(y)) return NAN;
    return (x > y) ? (x - y) : 0.0;
}

/* Decomposition and construction */

double frexp(double x, int *exp) {
    ieee_double_t u;
    u.d = x;
    if (isnan(x) || isinf(x) || x == 0.0) {
        *exp = 0;
        return x;
    }
    int e = (int)((u.u >> 52) & 0x7FF);
    if (e == 0) { /* Subnormal */
        x *= 4503599627370496.0; /* 2^52 */
        u.d = x;
        e = (int)((u.u >> 52) & 0x7FF) - 52;
    }
    e -= 1022;
    *exp = e;
    u.u &= 0x800FFFFFFFFFFFFFULL;
    u.u |= 0x3FE0000000000000ULL; /* Exponent for 0.5 */
    return u.d;
}

double ldexp(double x, int exp) {
    if (isnan(x) || isinf(x) || x == 0.0 || exp == 0) return x;
    double scale = 1.0;
    if (exp > 1023) {
        while (exp > 1023) {
            scale *= 8.9884656743115795e+307; /* ~2^1023 */
            exp -= 1023;
        }
    } else if (exp < -1022) {
        while (exp < -1022) {
            scale *= 2.2250738585072014e-308; /* ~2^-1022 */
            exp += 1022;
        }
    }
    ieee_double_t u;
    u.u = ((uint64_t)(exp + 1023)) << 52;
    return x * scale * u.d;
}

double modf(double x, double *iptr) {
    *iptr = trunc(x);
    return copysign(fabs(x) - fabs(*iptr), x);
}

/* Square root and hypotenuse */

double sqrt(double x) {
    if (isnan(x)) return x;
    if (x < 0.0) {
        errno = EDOM;
        return NAN;
    }
    if (x == 0.0 || isinf(x)) return x;
    double res;
#if defined(__x86_64__)
    __asm__ ("sqrtsd %1, %0" : "=x"(res) : "x"(x));
#else
    /* Newton-Raphson fallback */
    int exp;
    double val = frexp(x, &exp);
    res = 0.5 * (1.0 + val);
    if (exp & 1) {
        res *= 1.41421356237309504880;
        exp--;
    }
    for (int i = 0; i < 6; i++) {
        res = 0.5 * (res + val / res);
    }
    res = ldexp(res, exp / 2);
#endif
    return res;
}

float sqrtf(float x) {
    return (float)sqrt((double)x);
}

double cbrt(double x) {
    if (isnan(x) || isinf(x) || x == 0.0) return x;
    double sign = (x < 0.0) ? -1.0 : 1.0;
    double ax = fabs(x);
    double y = exp(log(ax) / 3.0);
    /* 2 Newton-Raphson refinement steps: y = (2*y + x / (y*y)) / 3 */
    y = (2.0 * y + ax / (y * y)) / 3.0;
    y = (2.0 * y + ax / (y * y)) / 3.0;
    return sign * y;
}

double hypot(double x, double y) {
    x = fabs(x);
    y = fabs(y);
    if (isinf(x) || isinf(y)) return INFINITY;
    if (isnan(x) || isnan(y)) return NAN;
    if (x == 0.0) return y;
    if (y == 0.0) return x;
    double max = (x > y) ? x : y;
    double min = (x > y) ? y : x;
    double r = min / max;
    return max * sqrt(1.0 + r * r);
}

/* Exponential and Logarithmic functions */

#define LN2_HI 0.6931471803691238
#define LN2_LO 1.9082149292705877e-10

double exp(double x) {
    if (isnan(x)) return x;
    if (x > 709.782712893384) {
        errno = ERANGE;
        return INFINITY;
    }
    if (x < -745.133219101941) return 0.0;
    if (x == 0.0) return 1.0;

    /* Argument reduction: x = k * ln(2) + r */
    double k_d = round(x * 1.4426950408889634); /* 1 / ln(2) */
    int k = (int)k_d;
    double r = x - k_d * LN2_HI - k_d * LN2_LO;

    /* Taylor series: e^r = 1 + r + r^2/2! + ... + r^12/12! */
    double r2 = r * r;
    double p = r + r2 * (0.5 + r * (0.16666666666666666 + r * (0.041666666666666664 +
               r * (0.008333333333333333 + r * (0.001388888888888889 + r * (0.0001984126984126984 +
               r * (0.0000248015873015873 + r * (0.00000275573192239859 + r * 2.755731922398589e-7))))))));
    return ldexp(1.0 + p, k);
}

float expf(float x) {
    return (float)exp((double)x);
}

double exp2(double x) {
    return exp(x * M_LN2);
}

double log(double x) {
    if (isnan(x)) return x;
    if (x < 0.0) {
        errno = EDOM;
        return NAN;
    }
    if (x == 0.0) {
        errno = ERANGE;
        return -INFINITY;
    }
    if (isinf(x)) return x;

    int exp;
    double f = frexp(x, &exp);
    /* Bring f into [sqrt(0.5), sqrt(2)] approx [0.707, 1.414] */
    if (f < 0.7071067811865475) {
        f *= 2.0;
        exp -= 1;
    }
    /* z = (f - 1) / (f + 1) */
    double z = (f - 1.0) / (f + 1.0);
    double z2 = z * z;
    /* log(f) = 2z * (1 + z^2/3 + z^4/5 + z^6/7 + z^8/9 + z^10/11 + z^12/13 + z^14/15) */
    double p = z * (2.0 + z2 * (0.6666666666666666 + z2 * (0.4 + z2 * (0.2857142857142857 +
               z2 * (0.2222222222222222 + z2 * (0.18181818181818182 + z2 * (0.15384615384615385 +
               z2 * 0.13333333333333333)))))));
    return p + (double)exp * M_LN2;
}

float logf(float x) {
    return (float)log((double)x);
}

double log10(double x) {
    return log(x) * M_LOG10E;
}

float log10f(float x) {
    return (float)log10((double)x);
}

double log2(double x) {
    return log(x) * M_LOG2E;
}

double pow(double x, double y) {
    if (isnan(x) || isnan(y)) return NAN;
    if (y == 0.0) return 1.0;
    if (x == 1.0) return 1.0;
    if (x == 0.0) {
        if (y < 0.0) {
            errno = ERANGE;
            return INFINITY;
        }
        return 0.0;
    }
    if (y == 1.0) return x;
    if (y == 2.0) return x * x;
    if (y == 0.5) return sqrt(x);

    /* Integer exponent check */
    int64_t iy = (int64_t)y;
    int is_int = ((double)iy == y);

    if (x < 0.0) {
        if (!is_int) {
            errno = EDOM;
            return NAN;
        }
        double res = exp(y * log(-x));
        return (iy & 1) ? -res : res;
    }

    return exp(y * log(x));
}

float powf(float x, float y) {
    return (float)pow((double)x, (double)y);
}

/* Trigonometric functions */

static double sin_poly(double r) {
    double r2 = r * r;
    return r * (1.0 + r2 * (-0.16666666666666666 + r2 * (0.008333333333333333 +
           r2 * (-0.0001984126984126984 + r2 * (0.000002755731922398589 +
           r2 * (-2.505210838544172e-8 + r2 * 1.6059043836821615e-10))))));
}

static double cos_poly(double r) {
    double r2 = r * r;
    return 1.0 + r2 * (-0.5 + r2 * (0.041666666666666664 +
           r2 * (-0.001388888888888889 + r2 * (0.0000248015873015873 +
           r2 * (-2.755731922398589e-7 + r2 * (2.08767569878681e-9 - r2 * 1.1470745597729725e-11))))));
}

double sin(double x) {
    if (isnan(x) || isinf(x)) return NAN;
    /* Reduce modulo 2*pi */
    double k_d = round(x * 0.6366197723675813); /* 2 / pi */
    int64_t k = (int64_t)k_d;
    double r = x - k_d * (M_PI_2);
    int quadrant = (int)(k & 3);
    if (quadrant < 0) quadrant += 4;
    switch (quadrant) {
        case 0: return sin_poly(r);
        case 1: return cos_poly(r);
        case 2: return -sin_poly(r);
        case 3: default: return -cos_poly(r);
    }
}

float sinf(float x) {
    return (float)sin((double)x);
}

double cos(double x) {
    if (isnan(x) || isinf(x)) return NAN;
    double k_d = round(x * 0.6366197723675813);
    int64_t k = (int64_t)k_d;
    double r = x - k_d * (M_PI_2);
    int quadrant = (int)(k & 3);
    if (quadrant < 0) quadrant += 4;
    switch (quadrant) {
        case 0: return cos_poly(r);
        case 1: return -sin_poly(r);
        case 2: return -cos_poly(r);
        case 3: default: return sin_poly(r);
    }
}

float cosf(float x) {
    return (float)cos((double)x);
}

double tan(double x) {
    double c = cos(x);
    if (c == 0.0) {
        errno = ERANGE;
        return (sin(x) >= 0.0) ? INFINITY : -INFINITY;
    }
    return sin(x) / c;
}

float tanf(float x) {
    return (float)tan((double)x);
}

/* Inverse trigonometric functions */

static double atan_poly(double z) {
    double z2 = z * z;
    return z * (1.0 + z2 * (-0.3333333333333333 + z2 * (0.2 + z2 * (-0.14285714285714285 +
           z2 * (0.1111111111111111 + z2 * (-0.09090909090909091 + z2 * (0.07692307692307693 +
           z2 * (-0.06666666666666667 + z2 * 0.058823529411764705))))))));
}

double atan(double x) {
    if (isnan(x)) return x;
    if (isinf(x)) return (x > 0.0) ? M_PI_2 : -M_PI_2;
    double ax = fabs(x);
    double res;
    if (ax > 2.414213562373095) { /* > tan(3*pi/8) */
        res = M_PI_2 - atan_poly(1.0 / ax);
    } else if (ax > 0.414213562373095) { /* > tan(pi/8) */
        res = M_PI_4 + atan_poly((ax - 1.0) / (ax + 1.0));
    } else {
        res = atan_poly(ax);
    }
    return (x < 0.0) ? -res : res;
}

float atanf(float x) {
    return (float)atan((double)x);
}

double atan2(double y, double x) {
    if (isnan(y) || isnan(x)) return NAN;
    if (x > 0.0) return atan(y / x);
    if (x < 0.0) {
        if (y >= 0.0) return atan(y / x) + M_PI;
        else return atan(y / x) - M_PI;
    }
    /* x == 0 */
    if (y > 0.0) return M_PI_2;
    if (y < 0.0) return -M_PI_2;
    return 0.0;
}

float atan2f(float y, float x) {
    return (float)atan2((double)y, (double)x);
}

double asin(double x) {
    if (isnan(x)) return x;
    if (x < -1.0 || x > 1.0) {
        errno = EDOM;
        return NAN;
    }
    if (x == 1.0) return M_PI_2;
    if (x == -1.0) return -M_PI_2;
    return atan2(x, sqrt(1.0 - x * x));
}

float asinf(float x) {
    return (float)asin((double)x);
}

double acos(double x) {
    if (isnan(x)) return x;
    if (x < -1.0 || x > 1.0) {
        errno = EDOM;
        return NAN;
    }
    return M_PI_2 - asin(x);
}

float acosf(float x) {
    return (float)acos((double)x);
}

/* Hyperbolic functions */

double sinh(double x) {
    if (isnan(x) || isinf(x)) return x;
    if (fabs(x) > 710.0) {
        errno = ERANGE;
        return (x > 0.0) ? INFINITY : -INFINITY;
    }
    double ex = exp(x);
    return 0.5 * (ex - 1.0 / ex);
}

double cosh(double x) {
    if (isnan(x) || isinf(x)) return fabs(x);
    if (fabs(x) > 710.0) {
        errno = ERANGE;
        return INFINITY;
    }
    double ex = exp(x);
    return 0.5 * (ex + 1.0 / ex);
}

double tanh(double x) {
    if (isnan(x)) return x;
    if (x > 20.0) return 1.0;
    if (x < -20.0) return -1.0;
    double e2x = exp(2.0 * x);
    return (e2x - 1.0) / (e2x + 1.0);
}
