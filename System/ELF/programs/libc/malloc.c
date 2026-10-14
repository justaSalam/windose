// malloc.c - first-fit allocator with boundary-tag coalescing. Memory comes from the kernel in chunks
// (k->alloc, pinned, zeroed, freed automatically when the program exits).
#include <stdlib.h>
#include <string.h>
#include "kernel.h"

typedef struct Hdr {
    uint32_t size;       // payload bytes (multiple of 16)
    uint32_t prev;       // payload bytes of the physically previous block in the chunk (0 if first)
    uint32_t flags;      // 1 = free, 2 = first in chunk, 4 = last in chunk
    uint32_t magic;
} Hdr;                   // 16 bytes, keeps payloads 16-aligned

#define F_FREE 1u
#define F_FIRST 2u
#define F_LAST 4u
#define MAGIC 0xA110C8EDu
#define HDR sizeof(Hdr)
#define MAX_CHUNKS 128
#define MIN_CHUNK (64u * 1024u)

static Hdr *chunks[MAX_CHUNKS];
static int nchunks;
static uint32_t last_chunk = MIN_CHUNK;

static Hdr *next_blk(Hdr *h) { return (h->flags & F_LAST) ? NULL : (Hdr *)((char *)h + HDR + h->size); }
static Hdr *prev_blk(Hdr *h) { return (h->flags & F_FIRST) ? NULL : (Hdr *)((char *)h - HDR - h->prev); }

static int grow(size_t need)
{
    if (nchunks >= MAX_CHUNKS) return 0;
    size_t min_sz = (need + HDR + 16 + 4095) & ~(size_t)4095;     // smallest chunk that satisfies the request
    size_t sz = last_chunk > min_sz ? last_chunk : min_sz;
    if (min_sz > 0x7FFF0000u) return 0;
    char *raw = __k->alloc(__k->pid, (int)sz);
    if (!raw && sz > min_sz) { sz = min_sz; raw = __k->alloc(__k->pid, (int)sz); }   // quota nearly used up
    if (!raw) return 0;
    size_t addr = ((size_t)raw + 15) & ~(size_t)15;
    size_t usable = ((size_t)(raw + sz) - addr) & ~(size_t)15;
    Hdr *h = (Hdr *)addr;
    h->size = (uint32_t)(usable - HDR);
    h->prev = 0;
    h->flags = F_FREE | F_FIRST | F_LAST;
    h->magic = MAGIC;
    chunks[nchunks++] = h;
    if (last_chunk < 8u * 1024u * 1024u) last_chunk *= 2;
    return 1;
}

static void split(Hdr *h, size_t n)
{
    if (h->size >= n + HDR + 16) {
        Hdr *r = (Hdr *)((char *)h + HDR + n);
        r->size = (uint32_t)(h->size - n - HDR);
        r->prev = (uint32_t)n;
        r->flags = F_FREE | (h->flags & F_LAST);
        r->magic = MAGIC;
        h->size = (uint32_t)n;
        h->flags &= ~F_LAST;
        Hdr *after = next_blk(r);
        if (after) after->prev = r->size;
    }
}

void *malloc(size_t n)
{
    if (n == 0) n = 1;
    if (n > 0x7FFF0000u) return NULL;
    n = (n + 15) & ~(size_t)15;
    for (int pass = 0; pass < 2; pass++) {
        for (int c = 0; c < nchunks; c++) {
            for (Hdr *h = chunks[c]; h; h = next_blk(h)) {
                if ((h->flags & F_FREE) && h->size >= n) {
                    split(h, n);
                    h->flags &= ~F_FREE;
                    return (char *)h + HDR;
                }
            }
        }
        if (pass == 0 && !grow(n)) return NULL;
    }
    return NULL;
}

void free(void *p)
{
    if (!p) return;
    Hdr *h = (Hdr *)((char *)p - HDR);
    if (h->magic != MAGIC || (h->flags & F_FREE)) abort();   // corrupt or double free
    h->flags |= F_FREE;
    Hdr *nx = next_blk(h);
    if (nx && (nx->flags & F_FREE)) {
        h->size += (uint32_t)(HDR + nx->size);
        h->flags |= (nx->flags & F_LAST);
        nx->magic = 0;
        Hdr *after = next_blk(h);
        if (after) after->prev = h->size;
    }
    Hdr *pv = prev_blk(h);
    if (pv && (pv->flags & F_FREE)) {
        pv->size += (uint32_t)(HDR + h->size);
        pv->flags |= (h->flags & F_LAST);
        h->magic = 0;
        Hdr *after = next_blk(pv);
        if (after) after->prev = pv->size;
    }
}

void *calloc(size_t n, size_t sz)
{
    if (sz && n > 0x7FFF0000u / sz) return NULL;
    size_t t = n * sz;
    void *p = malloc(t);
    if (p) memset(p, 0, t);
    return p;
}

void *realloc(void *p, size_t n)
{
    if (!p) return malloc(n);
    if (n == 0) { free(p); return NULL; }
    Hdr *h = (Hdr *)((char *)p - HDR);
    if (h->magic != MAGIC) abort();
    size_t need = (n + 15) & ~(size_t)15;
    if (h->size >= need) { split(h, need); return p; }
    void *q = malloc(n);
    if (!q) return NULL;
    memcpy(q, p, h->size);
    free(p);
    return q;
}
