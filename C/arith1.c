
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "errhand.h"
#include "bitio.h"


typedef struct {
    unsigned short int low_count;
    unsigned short int high_count;
    unsigned short int scale;
} SYMBOL;

#define MAXIMUM_SCALE   16383  /* Maximum allowed frequency count        */
#define END_OF_STREAM   256    /* The EOF symbol                         */

extern long underflow_bits;    /* The present underflow count in         */
                               /* the arithmetic coder.                  */
int *totals[ 257 ];            /* Pointers to the 257 context tables     */


void initialize_arithmetic_decoder( BIT_FILE *stream );
void remove_symbol_from_stream( BIT_FILE *stream, SYMBOL *s );
void initialize_arithmetic_encoder( void );
void encode_symbol( BIT_FILE *stream, SYMBOL *s );
void flush_arithmetic_encoder( BIT_FILE *stream );
short int get_current_count( SYMBOL *s );
void initialize_model( void );
void update_model( int symbol, int context );
void convert_int_to_symbol( int symbol, int context, SYMBOL *s );
void get_symbol_scale( int context, SYMBOL *s );
int convert_symbol_to_int( int count, int context, SYMBOL *s );

char *CompressionName = "Adaptive order 1 model with arithmetic coding";
char *Usage           = "in-file out-file\n\n";

void CompressFile ( FILE *input,  BIT_FILE *output,  int argc,  char *argv[] )
{
    SYMBOL s;
    int c;
    int context;

    context = 0;
    initialize_model();
    initialize_arithmetic_encoder();
    for ( ; ; ) {
        c = getc( input );
        if ( c == EOF )
            c = END_OF_STREAM;
        convert_int_to_symbol( c, context, &s );
        encode_symbol( output, &s );
        if ( c == END_OF_STREAM )
	    break;
        update_model( c, context );
        context = c;
    }
    flush_arithmetic_encoder( output );
    putchar( '\n' );
    while ( argc-- > 0 )
        printf( "Unknown argument: %s\n", *argv++ );
}

void ExpandFile ( BIT_FILE *input,  FILE *output,  int argc,  char *argv[] )
{
    SYMBOL s;
    int count;
    int c;
    int context;

    context = 0;
    initialize_model();
    initialize_arithmetic_decoder( input );
    for ( ; ; ) {
        get_symbol_scale( context, &s );
        count = get_current_count( &s );
        c = convert_symbol_to_int( count, context, &s );
        remove_symbol_from_stream( input, &s );
        if ( c == END_OF_STREAM )
            break;
        putc( (char) c, output );
        update_model( c, context );
        context = c;
    }
    putchar( '\n' );
    while ( argc-- > 0 )
        printf( "Unknown argument: %s\n", *argv++ );
}


void initialize_model ()
{
    int context;
    int i;

    for ( context = 0 ; context < END_OF_STREAM ; context++ ) {
        totals[ context ] = (int *) calloc( END_OF_STREAM + 2, sizeof(int) );
        if ( totals[ context ] == NULL )
            fatal_error( "Failure allocating context %d", context );
        for ( i = 0 ; i <= ( END_OF_STREAM + 1 ) ; i++ )
            totals[ context ][ i ] = i;
    }
}


void update_model ( int symbol,  int context )
{
    int i;

    for ( i = symbol + 1 ; i <= ( END_OF_STREAM + 1 ) ; i++ )
        totals[ context ][ i ]++;
    if ( totals[ context ][ END_OF_STREAM + 1 ] < MAXIMUM_SCALE )
        return;
    for ( i = 1 ; i <= ( END_OF_STREAM + 1 ) ; i++ ) {
        totals[ context ][ i ] /= 2;
        if ( totals[ context ][ i ] <= totals[ context ][ i - 1 ] )
            totals[ context ][ i ] = totals[ context ][ i - 1 ] + 1;
    }
}

void convert_int_to_symbol ( int c,  int context,  SYMBOL *s )
{
    s->scale = totals[ context ][ END_OF_STREAM + 1 ];
    s->low_count = totals[ context ][ c ];
    s->high_count = totals[ context ][ c + 1 ];
}


void get_symbol_scale ( int context,  SYMBOL *s )
{
    s->scale = totals[ context][ END_OF_STREAM + 1 ];
}

int convert_symbol_to_int ( int count,  int context,  SYMBOL *s )
{
    int c;

    for ( c = 0; count >= totals[ context ][ c + 1 ] ; c++ )
        ;
    s->high_count = totals[ context ][ c + 1 ];
    s->low_count = totals[ context ][ c ];
    return( c );
}

static unsigned short int code;  /* The present input code value       */
static unsigned short int low;   /* Start of the current code range    */
static unsigned short int high;  /* End of the current code range      */
long underflow_bits;             /* Number of underflow bits pending   */

void initialize_arithmetic_encoder ()
{
    low = 0;
    high = 0xffff;
    underflow_bits = 0;
}

void flush_arithmetic_encoder ( BIT_FILE *stream )
{
    OutputBit( stream, low & 0x4000 );
    underflow_bits++;
    while ( underflow_bits-- > 0 )
        OutputBit( stream, ~low & 0x4000 );
    OutputBits( stream, 0L, 16 );
}

void encode_symbol ( BIT_FILE *stream,  SYMBOL *s )
{
    long range;
    range = (long) ( high-low ) + 1;
    high = low + (unsigned short int)
                 (( range * s->high_count ) / s->scale - 1 );
    low = low + (unsigned short int)
                 (( range * s->low_count ) / s->scale );
    for ( ; ; ) {
        if ( ( high & 0x8000 ) == ( low & 0x8000 ) ) {
            OutputBit( stream, high & 0x8000 );
            while ( underflow_bits > 0 ) {
                OutputBit( stream, ~high & 0x8000 );
                underflow_bits--;
            }
        }
        else if ( ( low & 0x4000 ) && !( high & 0x4000 )) {
            underflow_bits += 1;
            low &= 0x3fff;
            high |= 0x4000;
        } else
            return ;
        low <<= 1;
        high <<= 1;
        high |= 1;
    }
}

short int get_current_count( SYMBOL *s )
{
    long range;
    short int count;

    range = (long) ( high - low ) + 1;
    count = (short int)
            ((((long) ( code - low ) + 1 ) * s->scale-1 ) / range );
    return( count );
}

void initialize_arithmetic_decoder ( BIT_FILE *stream )
{
    int i;

    code = 0;
    for ( i = 0 ; i < 16 ; i++ ) {
        code <<= 1;
        code += InputBit( stream );
    }
    low = 0;
    high = 0xffff;
}

void remove_symbol_from_stream ( BIT_FILE *stream,  SYMBOL *s )
{
    long range;

    range = (long)( high - low ) + 1;
    high = low + (unsigned short int)
                 (( range * s->high_count ) / s->scale - 1 );
    low = low + (unsigned short int)
                 (( range * s->low_count ) / s->scale );
    for ( ; ; ) {
        if ( ( high & 0x8000 ) == ( low & 0x8000 ) ) {
        }
        else if ((low & 0x4000) == 0x4000  && (high & 0x4000) == 0 ) {
            code ^= 0x4000;
            low   &= 0x3fff;
            high  |= 0x4000;
        } else
 
            return;
        low <<= 1;
        high <<= 1;
        high |= 1;
        code <<= 1;
        code += InputBit( stream );
    }
}


