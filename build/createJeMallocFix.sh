#!/bin/bash
if [ ! -f ../output/debug/jemallocfix.so ]
then
  # jemallocfix for firefox 10+
  echo -e "#include<stdlib.h>\nsize_t je_malloc_usable_size_in_advance(size_t n){ return n; }\nvoid * moz_xrealloc(void *ptr, size_t size) { return realloc(ptr, size); }" | gcc -xc -fPIC --shared - -o ../output/debug/jemallocfix.so
  strip ../output/debug/jemallocfix.so
fi
