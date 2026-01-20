#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <math.h>
#include "bitio.h"
#include "errhand.h"
#include "main.h"

char *CompressionName = "Sound sample companding";
char *Usage =
"infile outfile [n]\n\n n optionally sets the bits per sample\n\n";

long get_file_length( FILE *file );

#ifndef SEEK_END
#define SEEK_END 2
#endif

#ifndef SEEK_SET
#define SEEK_SET 0
#endif

void CompressFile ( FILE *input,  BIT_FILE *output,  int argc,  char *argv[] )
{
    int compress[ 256 ];
    int steps;
    int bits;
    int value;
    int i;
    int j;
    int c;

    if ( argc-- > 0 )
        bits = atoi( *argv );
    else
        bits = 4;
    printf( "Compressing using %d bits per sample...\n", bits );
    steps = ( 1 << ( bits - 1 ) );
    OutputBits( output, (unsigned long) bits, 8 );
    OutputBits( output, (unsigned long) get_file_length( input ), 32 );
    for ( i = steps ; i > 0; i-- ) {
        value = (int)
           ( 128.0 * ( pow( 2.0, (double) i  /  steps ) - 1.0 ) + 0.5 );
        for ( j = value ; j > 0 ; j-- ) {
            compress[ j + 127 ] = i + steps - 1;
            compress[ 128 - j ] = steps - i;
        }
    }
    while ( ( c = getc( input ) ) != EOF )
        OutputBits( output, (unsigned long) compress[ c ], bits );
}


void ExpandFile ( BIT_FILE *input,  FILE *output,  int argc,  char *argv[] )
{
    int steps;
    int bits;
    int value;
    int last_value;
    int i;
    int c;
    long count;
    int expand[ 256 ];

    bits = (int) InputBits( input, 8 );
    printf( "Expanding using %d bits per sample...\n", bits );
    steps = ( 1 << ( bits - 1 ) );
    last_value = 0;
    for ( i = 1; i <= steps; i++ ) {
        value = (int)
            ( 128.0 * ( pow( 2.0, (double) i  /  steps ) - 1.0 ) + 0.5 );
        expand[ steps + i - 1 ] = 128 + ( value + last_value ) / 2;
        expand[ steps - i ] = 127 - ( value + last_value ) / 2;
        last_value = value;
    }
    for ( count = InputBits( input, 32 ) ; count > 0 ; count-- ) {
        c = (int) InputBits( input, bits );
        putc( expand[ c ], output );
    }
    while ( argc-- > 0 )
        printf( "Unused argument: %s\n", *argv++ );
}


long get_file_length ( FILE *file )
{
    long marker;
    long eof_ftell;

    marker = ftell( file );
    fseek( file, 0L, SEEK_END );
    eof_ftell = ftell( file );
    fseek( file, marker, SEEK_SET );
    return( eof_ftell - marker );
}

