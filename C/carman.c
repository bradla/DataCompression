
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <stdarg.h>
//#include <varargs.h>

  /* All Borland C/C++ versions */
  #ifdef __TURBOC__
    #define MSDOS 1
    #include <io.h>
    #include <dir.h>
    #define DIR_STRUCT  struct ffblk
    #define FIND_FIRST( n, d, a ) findfirst( n, d, a )
    #define FIND_NEXT findnext
    #define DIR_FILE_NAME ff_name
  #endif
   /* Microsoft, Watcom, Zortech */
  #if defined( M_I86 ) || defined ( __ZTC__ ) || defined ( __TSC__ )
    #define MSDOS 1
    #include <dos.h>
    #define DIR_STRUCT struct find_t
    #define FIND_FIRST( n, d, a ) _dos_findfirst( n, a, d )
    #define FIND_NEXT _dos_findnext
    #define DIR_FILE_NAME name
  #endif

#define BASE_HEADER_SIZE   19
#define CRC_MASK           0xFFFFFFFFL
#define CRC32_POLYNOMIAL   0xEDB88320L

#ifndef FILENAME_MAX
#define FILENAME_MAX 128
#endif

typedef struct header {
    char file_name[ FILENAME_MAX ];
    char compression_method;
    unsigned long original_size;
    unsigned long compressed_size;
    unsigned long original_crc;
    unsigned long header_crc;
} HEADER;

void FatalError( char *message, ... );
void BuildCRCTable( void );
unsigned long CalculateBlockCRC32( unsigned int count,
                                   unsigned long crc,
                                   void *buffer );
unsigned long UpdateCharacterCRC32( unsigned long crc, int c );
int ParseArguments( int argc, char *argv[] );
void UsageExit( void );
void OpenArchiveFiles( char *name, int command );
void BuildFileList( int argc, char *argv[], int command );
int ExpandAndMassageMSDOSFileNames( int count, char *wild_name );
void MassageMSDOSFileName( int count, char *file );
int AddFileListToArchive( void );
int ProcessAllFilesInInputCar( int command, int count );
int SearchFileList( char *file_name );
int WildCardMatch( char *s1, char *s2 );
void SkipOverFileFromInputCar( void );
void CopyFileFromInputCar( void );
void PrintListTitles( void );
void ListCarFileEntry( void );
int RatioInPercent( unsigned long compressed, unsigned long original );
int ReadFileHeader( void );
unsigned long UnpackUnsignedData( int number_of_bytes, unsigned char *buffer );
void WriteFileHeader( void );
void PackUnsignedData( int number_of_bytes, unsigned long number, unsigned char *buffer );
void WriteEndOfCarHeader( void );
void Insert( FILE *input_text_file, char *operation );
void Extract( FILE *destination );
int Store( FILE *input_text_file );
unsigned long Unstore( FILE *destination );
int LZSSCompress( FILE *input_text_file );
unsigned long LZSSExpand( FILE *destination );


char TempFileName[ FILENAME_MAX ];   /* The output archive is first    */
				      /* opened with a temporary name   */

FILE *InputCarFile;		      /* The input CAR file.  This file */
				      /* may not exist for 'A' commands */

char CarFileName[ FILENAME_MAX ];     /* Name of the CAR file, defined  */
				      /* on the command line            */

FILE *OutputCarFile;		      /* The output CAR, only exists for*/
				      /* the 'A' and 'R' operations     */

HEADER Header;			      /* The Header block for the file  */
				      /* presently being operated on    */

char *FileList[ 100 ];		      /* The list of file names passed  */
				      /* on the command line            */

unsigned long Ccitt32Table[ 256 ];    /* This array holds the CRC	*/
				      /* table used to calculate the 32 */
				      /* bit CRC values.                */

int main( int argc,  char *argv[] )
{
    int command;
    int count;

    setbuf( stdout, NULL );
    setbuf( stderr, NULL );
    fprintf( stderr, "CARMAN 1.0 : " );
    BuildCRCTable();
    command = ParseArguments( argc, argv );
    fprintf( stderr, "\n" );
    OpenArchiveFiles( argv[ 2 ], command );
    BuildFileList( argc - 3, argv + 3, command );
    if ( command == 'A' )
	count = AddFileListToArchive();
    else
	count = 0;
    if ( command == 'L' )
	PrintListTitles();
    count = ProcessAllFilesInInputCar( command, count );
    if ( OutputCarFile != NULL && count != 0 ) {
	WriteEndOfCarHeader();
	if ( ferror( OutputCarFile ) || fclose( OutputCarFile ) == EOF )
	    FatalError( "Can't write" );

    remove( CarFileName );
    rename( TempFileName, CarFileName );
    //unlink( CarFileName );
    //link( TempFileName, CarFileName );
    //unlink( TempFileName );
    }
    if ( command != 'P' )
        printf( "\n%d file%s\n", count, ( count == 1 ) ? "" : "s" );
    else
        fprintf( stderr, "\n%d file%s\n", count, ( count == 1 ) ? "" : "s" );
    return( 0 );
}

void FatalError( char *fmt, ... )
{
    va_list args;

    va_start( args, fmt );

    putc( '\n', stderr );
    vfprintf( stderr, fmt, args );
    putc( '\n', stderr );
    va_end( args );
    if ( OutputCarFile != NULL ) {
        fclose( OutputCarFile );
        remove( TempFileName );
    }
    exit( 1 );
}

void BuildCRCTable ()
{
    int i;
    int j;
    unsigned long value;

    for ( i = 0; i <= 255 ; i++ ) {
	value = i;
	for ( j = 8 ; j > 0; j-- ) {
	    if ( value & 1 )
		value = ( value >> 1 ) ^ CRC32_POLYNOMIAL;
	    else
		value >>= 1;
	}
	Ccitt32Table[ i ] = value;
    }
}


unsigned long CalculateBlockCRC32 ( unsigned int count,  unsigned long crc,  void *buffer )
{
    unsigned char *p = (unsigned char *) buffer;
    unsigned long temp1;
    unsigned long temp2;

    while ( count-- != 0 ) {
	temp1 = ( crc >> 8 ) & 0x00FFFFFFL;
	temp2 = Ccitt32Table[ ( (int) crc ^ *p++ ) & 0xff ];
	crc = temp1 ^ temp2;
    }
    return( crc );
}


unsigned long UpdateCharacterCRC32 ( unsigned long crc,  int c )
{
    unsigned long temp1;
    unsigned long temp2;

    temp1 = ( crc >> 8 ) & 0x00FFFFFFL;
    temp2 = Ccitt32Table[ ( (int) crc ^ c ) & 0xff ];
    crc = temp1 ^ temp2;
    return( crc );
}


int ParseArguments ( int argc,  char *argv[] )
{
    int command;

    if ( argc < 3 || strlen( argv[ 1 ] ) > 1 )
	UsageExit();
    switch( command = toupper( argv[ 1 ][ 0 ] ) ) {
	case 'X' :
	    fprintf( stderr, "Extracting files\n" );
	    break;
	case 'R' :
	    fprintf( stderr, "Replacing files\n" );
	    break;
	case 'P' :
	    fprintf( stderr, "Print files to stdout\n" );
	    break;
	case 'T' :
	    fprintf( stderr, "Testing integrity of files\n" );
	    break;
	case 'L' :
	    fprintf( stderr, "Listing archive contents\n" );
	    break;
	case 'A' :
	    if ( argc <= 3 )
		UsageExit();
	    fprintf( stderr, "Adding/replacing files to archive\n" );
	    break;
	case 'D' :
	    if ( argc <= 3 )
		UsageExit();
	    fprintf( stderr, "Deleting files from archive\n" );
	    break;
	default  :
	    UsageExit();
    };
    return( command );
}


void UsageExit ()
{
    fputs( "CARMAN -- Compressed ARchive MANager\n", stderr );
    fputs( "Usage: carman command car-file [file ...]\n", stderr );
    fputs( "Commands:\n", stderr );
    fputs( "  a: Add files to a CAR archive (replace if present)\n", stderr );
    fputs( "  x: Extract files from a CAR archive\n", stderr );
    fputs( "  r: Replace files in a CAR archive\n", stderr );
    fputs( "  d: Delete files from a CAR archive\n", stderr );
    fputs( "  p: Print files on standard output\n", stderr );
    fputs( "  l: List contents of a CAR archive\n", stderr );
    fputs( "  t: Test files in a CAR archive\n", stderr );
    fputs( "\n", stderr );
    exit( 1 );
}


void OpenArchiveFiles ( char *name,  int command )
{
    char *s;
    int i;

    strncpy( CarFileName, name, FILENAME_MAX - 1 );
    CarFileName[ FILENAME_MAX - 1 ] = '\0';
    InputCarFile = fopen( CarFileName, "rb" );
    if ( InputCarFile == NULL ) {
#ifdef MSDOS
	s = strrchr( CarFileName, '\\' );
#else  /* UNIX */
	s = strrchr( CarFileName, '/' );
#endif
	if ( s == NULL )
	    s = CarFileName;
	if ( strrchr( s, '.' ) == NULL )
	    if ( strlen( CarFileName ) < ( FILENAME_MAX - 4 ) ) {
		strcat( CarFileName, ".car" );
		InputCarFile = fopen( CarFileName, "rb" );
	    }
    }
    if ( InputCarFile == NULL && command != 'A' )
        FatalError( "Can't open archive '%s'", CarFileName );
    if ( command == 'A' || command == 'R' || command == 'D' ) {
        strcpy( TempFileName, CarFileName );
        s = strrchr( TempFileName, '.' );
        if ( s == NULL )
            s = TempFileName + strlen( TempFileName );
        for ( i = 0 ; i < 10 ; i++ ) {
            sprintf( s, ".$$%d", i );
            if ( ( OutputCarFile = fopen( TempFileName, "r" ) ) == NULL )
                break;
            fclose( OutputCarFile );
            OutputCarFile = NULL;
        }
        if ( i == 10 )
            FatalError( "Can't open temporary file %s", TempFileName );
	OutputCarFile = fopen( TempFileName, "wb" );
	if ( OutputCarFile == NULL )
	    FatalError( "Can't open temporary file %s", TempFileName );
    };
    if ( InputCarFile != NULL )
        setvbuf( InputCarFile, NULL, _IOFBF, 8192 );
    if ( OutputCarFile != NULL )
        setvbuf( OutputCarFile, NULL, _IOFBF, 8192 );
}


void BuildFileList ( int argc,  char *argv[],  int command )
{
    int i;
    int count;

    count = 0;
    if ( argc == 0 )
	FileList[ count++ ] = "*";
    else {
	for ( i = 0 ; i < argc ; i++ ) {
#ifdef MSDOS
            if ( command == 'A' )
                count = ExpandAndMassageMSDOSFileNames( count, argv[ i ] );
            else
                MassageMSDOSFileName( count++, argv[ i ] );
#endif
#ifndef MSDOS
	    FileList[ count ] = malloc( strlen( argv[ i ] ) + 2 );
            if ( FileList[ count ] == NULL )
                FatalError( "Ran out of memory storing file names" );
	    strcpy( FileList[ count++ ], argv[ i ] );
#endif
            if ( count > 99 )
                FatalError( "Too many file names" );
	}
    }
    FileList[ count ] = NULL;
}

#ifdef MSDOS
int ExpandAndMassageMSDOSFileNames ( int count,  char *wild_name )
{
    int done;
    DIR_STRUCT file_info_block;
    char *leading_path;
    char *file_name;
    char *p;

    leading_path = malloc( strlen( wild_name ) + 1 );
    file_name = malloc( strlen( wild_name ) + 13 );
    if ( leading_path == NULL || file_name == NULL )
        FatalError( "Ran out of memory storing file names" );
    strcpy( leading_path, wild_name );
    p = strrchr( leading_path, '\\' );
    if ( p != NULL )
        p[ 1 ] = '\0';
    else {
        p = strrchr( leading_path, ':' );
        if ( p != NULL )
            p[ 1 ] = '\0';
        else
            leading_path[ 0 ] = '\0';
    }
    done = FIND_FIRST( wild_name, &file_info_block, 0 );
    while ( !done ) {
        strcpy( file_name, leading_path );
        strcat( file_name, file_info_block.DIR_FILE_NAME );
        MassageMSDOSFileName( count++, file_name );
        done = FIND_NEXT( &file_info_block );
        if ( count > 99 )
            FatalError( "Too many file names" );
    }
    free( leading_path );
    free( file_name );
    return( count );
}


void MassageMSDOSFileName ( int count,  char *file )
{
    int i;
    char *p;

    FileList[ count ] = malloc( strlen( file ) + 2 );
    if ( FileList[ count ] == NULL )
        FatalError( "Ran out of memory storing file names" );
    strcpy( FileList[ count ], file );
    for ( i = 0 ; FileList[ count ][ i ] != '\0' ; i++ )
        FileList[ count ][ i ] = (char) tolower( FileList[ count ][ i ] );
    if ( strpbrk( FileList[ count ], "*?" ) == NULL ) {
	p = strrchr( FileList[ count ], '\\' );
	if ( p == NULL )
	    p = FileList[ count ];
	if ( strrchr( p, '.' ) == NULL )
	    strcat( FileList[ count ], "." );
    }
}
#endif

int AddFileListToArchive ()
{
    int i;
    int j;
    int skip;
    char *s;
    FILE *input_text_file;

    for ( i = 0 ; FileList[ i ] != NULL ; i++ ) {
	input_text_file = fopen( FileList[ i ], "rb" );
	if ( input_text_file == NULL )
	    FatalError( "Could not open %s to add to CAR file", FileList[ i ] );
#ifdef MSDOS
	s = strrchr( FileList[ i ], '\\' );
	if ( s == NULL )
	    s = strrchr( FileList[ i ], ':' );
#endif
#ifndef MSDOS       /* Must be UNIX */
	s = strrchr( FileList[ i ], '/' );
#endif
        if ( s != NULL )
            s++;
        else
           s = FileList[ i ];
        skip = 0;
	for ( j = 0 ; j < i ; j++ )
	    if ( strcmp( s, FileList[ j ] ) == 0 ) {
		fprintf( stderr, "Duplicate file name: %s", FileList[ i ] );
                fprintf( stderr, "   Skipping this file...\n" );
		skip = 1;
                break;
             }
        if ( s != FileList[ i ] ) {
    	    for ( j = 0 ; s[ j ] != '\0' ; j++ )
        	FileList[ i ][ j ] = s[ j ];
	    FileList[ i ][ j ] = '\0';
        }
        if ( !skip ) {
            strcpy( Header.file_name, FileList[ i ] );
            Insert( input_text_file, "Adding" );
        } else
            fclose( input_text_file );
    }
    return( i );
}


int ProcessAllFilesInInputCar ( int command,  int count )
{
    int matched;
    FILE *input_text_file;
    FILE *output_destination;

    if ( command == 'P' )
        output_destination = stdout;
    else if ( command == 'T' )
#ifdef MSDOS
        output_destination = fopen( "NUL", "wb" );
#else
        output_destination = fopen( "/dev/null", "wb" );
#endif
    else
        output_destination = NULL;
    while ( InputCarFile != NULL && ReadFileHeader() != 0 ) {
        matched = SearchFileList( Header.file_name );
        switch ( command ) {
            case 'D' :
                 if ( matched ) {
                     SkipOverFileFromInputCar();
                     count++;
                 } else
                     CopyFileFromInputCar();
                 break;
            case 'A' :
                 if ( matched )
                     SkipOverFileFromInputCar();
                 else
                     CopyFileFromInputCar();
                 break;
            case 'L' :
                if ( matched ) {
                    ListCarFileEntry();
                    count++;
                }
                SkipOverFileFromInputCar();
                break;
            case 'P' :
            case 'X' :
            case 'T' :
                if ( matched ) {
                    Extract( output_destination );
                    count++;
                } else
                    SkipOverFileFromInputCar();
                break;
            case 'R' :
                if ( matched ) {
                    input_text_file = fopen( Header.file_name, "rb" );
                    if ( input_text_file == NULL ) {
                       fprintf( stderr, "Could not find %s", Header.file_name );
                       fprintf( stderr, " for replacement, skipping\n" );
                       CopyFileFromInputCar();
                    } else {
                        SkipOverFileFromInputCar();
                        Insert( input_text_file, "Replacing" );
                        count++;
                        fclose( input_text_file );
                    }
                } else
                    CopyFileFromInputCar();
                break;
        }
    }
    return( count );
}



int SearchFileList ( char *file_name )
{
    int i;

    for ( i = 0 ; FileList[ i ] != NULL ; i++ ) {
	if ( WildCardMatch( file_name, FileList[ i ] ) )
            return( 1 );
    }
    return( 0 );
}


int WildCardMatch ( char *string,  char *wild_string )
{
    for ( ; ; ) {
        if ( *wild_string == '*' ) {
            wild_string++;
            for ( ; ; ) {
                while ( *string != '\0' && *string != *wild_string )
                    string++;
                if ( WildCardMatch( string, wild_string ) )
                    return( 1 );
                else if ( *string == '\0' )
                    return( 0 );
                else
                    string++;
            }
        } else if ( *wild_string == '?' ) {
            wild_string++;
            if ( *string++ == '\0' )
                return( 0 );
        } else {
            if ( *string != *wild_string )
                return( 0 );
            if ( *string == '\0' )
                return( 1 );
            string++;
            wild_string++;
        }
    }
}


void SkipOverFileFromInputCar ()
{
	fseek( InputCarFile, Header.compressed_size, SEEK_CUR );
}


void CopyFileFromInputCar ()
{
    char buffer[ 256 ];
    unsigned int count;

    WriteFileHeader();
    while ( Header.compressed_size != 0 ) {
        if ( Header.compressed_size < 256 )
            count = (int) Header.compressed_size;
        else
            count = 256;
        if ( fread( buffer, 1, count, InputCarFile ) != count )
	    FatalError( "Error reading input file %s", Header.file_name );
        Header.compressed_size -= count;
	if ( fwrite( buffer, 1, count, OutputCarFile) != count )
	    FatalError( "Error writing to output CAR file" );
    }
}


void PrintListTitles ()
{
    printf( "\n" );
    printf( "                       Original  Compressed\n" );
    printf( "     Filename            Size       Size     Ratio   CRC-32   Method\n" );
    printf( "------------------     --------  ----------  -----  --------  ------\n" );
}


void ListCarFileEntry ()
{
    static char *methods[] = {
	"Stored",
        "LZSS"
    };

    printf( "%-20s %10lu  %10lu  %4d%%  %08lx  %s\n",
            Header.file_name,
            Header.original_size,
            Header.compressed_size,
            RatioInPercent( Header.compressed_size, Header.original_size ),
            Header.original_crc,
            methods[ Header.compression_method - 1 ] );
}


int RatioInPercent ( unsigned long compressed,  unsigned long original )
{
    int result;

    if ( original == 0 )
        return( 0 );
    result = (int) ( ( 100L * compressed ) / original );
    return( 100 - result );
}


int ReadFileHeader ()
{
    unsigned char header_data[ 17 ];
    unsigned long header_crc;
    int i;
    int c;

    for ( i = 0 ; ; ) {
        c = getc( InputCarFile );
        Header.file_name[ i ] = (char) c;
        if ( c == '\0' )
            break;
        if ( ++i == FILENAME_MAX )
            FatalError( "File name exceeded maximum in header" );
    }
    if ( i == 0 )
       return( 0 );
    header_crc = CalculateBlockCRC32( i + 1, CRC_MASK, Header.file_name );
    fread( header_data, 1, 17, InputCarFile );
    Header.compression_method = (char)
                                UnpackUnsignedData( 1, header_data + 0  );
    Header.original_size      = UnpackUnsignedData( 4, header_data + 1  );
    Header.compressed_size    = UnpackUnsignedData( 4, header_data + 5  );
    Header.original_crc       = UnpackUnsignedData( 4, header_data + 9  );
    Header.header_crc         = UnpackUnsignedData( 4, header_data + 13 );
    header_crc = CalculateBlockCRC32( 13, header_crc, header_data );
    header_crc ^= CRC_MASK;
    if ( Header.header_crc != header_crc )
        FatalError( "Header checksum error for file %s", Header.file_name );
    return( 1 );
}


unsigned long UnpackUnsignedData ( int number_of_bytes,  unsigned char *buffer )
{
    unsigned long result;
    int shift_count;

    result = 0;
    shift_count = 0;
    while ( number_of_bytes-- > 0 ) {
	result |= (unsigned long) *buffer++ << shift_count;
        shift_count += 8;
    }
    return( result );
}


void WriteFileHeader ()
{
    unsigned char header_data[ 17 ];
    int i;

    for ( i = 0 ; ; ) {
        putc( Header.file_name[ i ], OutputCarFile );
        if ( Header.file_name[ i++ ] == '\0' )
            break;
    }
    Header.header_crc = CalculateBlockCRC32( i, CRC_MASK, Header.file_name );
    PackUnsignedData( 1, (long)
                         Header.compression_method, header_data + 0  );
    PackUnsignedData( 4, Header.original_size,      header_data + 1 );
    PackUnsignedData( 4, Header.compressed_size,    header_data + 5 );
    PackUnsignedData( 4, Header.original_crc,       header_data + 9 );
    Header.header_crc = CalculateBlockCRC32( 13, Header.header_crc,
                                             header_data );
    Header.header_crc ^= CRC_MASK;
    PackUnsignedData( 4, Header.header_crc, header_data + 13 );
    fwrite( header_data, 1, 17, OutputCarFile );
}

void PackUnsignedData ( int number_of_bytes,  unsigned long number,  unsigned char *buffer )
{
    while ( number_of_bytes-- > 0 ) {
        *buffer++ = ( unsigned char ) ( number & 0xff );
        number >>= 8;
    }
}

void WriteEndOfCarHeader ()
{
    fputc( 0, OutputCarFile );
}

void Insert ( FILE *input_text_file,  char *operation )
{
    long saved_position_of_header;
    long saved_position_of_file;

    fprintf( stderr, "%s %-20s", operation, Header.file_name );
    saved_position_of_header = ftell( OutputCarFile );
    Header.compression_method = 2;
    WriteFileHeader();
    saved_position_of_file = ftell(OutputCarFile);
    fseek( input_text_file, 0L, SEEK_END );
    Header.original_size = ftell( input_text_file );
    fseek( input_text_file, 0L, SEEK_SET );
    if ( !LZSSCompress( input_text_file ) ) {
        Header.compression_method = 1;
	fseek( OutputCarFile, saved_position_of_file, SEEK_SET );
        rewind( input_text_file );
        Store( input_text_file );
    }
    fclose( input_text_file );
    fseek( OutputCarFile, saved_position_of_header, SEEK_SET );
    WriteFileHeader();
    fseek( OutputCarFile, 0L, SEEK_END );
    printf( " %d%%\n", RatioInPercent( Header.compressed_size, Header.original_size ) );
}


void Extract ( FILE *destination )
{
    FILE *output_text_file;
    unsigned long crc;
    int error;

    fprintf( stderr, "%-20s ", Header.file_name );
    error = 0;
    if ( destination == NULL ) {
	if ( ( output_text_file = fopen(Header.file_name, "wb") ) == NULL ) {
	    fprintf( stderr, "Can't open %s\n", Header.file_name );
	    fprintf( stderr, "Not extracted\n" );
	    SkipOverFileFromInputCar();
            return;
	}
    } else
	output_text_file = destination;
    switch ( Header.compression_method ) {
        case 1 :
            crc = Unstore( output_text_file );
            break;
        case 2 :
            crc = LZSSExpand( output_text_file );
            break;
        default :
            fprintf( stderr, "Unknown method: %c\n",
                     Header.compression_method );
            SkipOverFileFromInputCar();
            error = 1;
            crc =  Header.original_crc;
	    break;
    }
    if ( crc != Header.original_crc ) {
	fprintf( stderr, "CRC error reading data\n" );
        error = 1;
    }
    if ( destination == NULL ) {
        fclose( output_text_file );
	if ( error )
            remove( Header.file_name );
            //unlink( Header.file_name );
    }
    if ( !error )
        fprintf( stderr, " OK\n" );
}


int Store ( FILE *input_text_file )
{
    unsigned int n;
    char buffer[ 256 ];
    int pacifier;

    pacifier = 0;
    Header.original_crc = CRC_MASK;
    while ( ( n = fread( buffer, 1, 256, input_text_file ) ) != 0 ) {
	fwrite( buffer, 1, n, OutputCarFile );
        Header.original_crc = CalculateBlockCRC32( n, Header.original_crc, buffer );
        if ( ( ++pacifier & 15 ) == 0 )
            putc( '.', stderr );
    }
    Header.compressed_size = Header.original_size;
    Header.original_crc ^= CRC_MASK;
    return( 1 );
}

unsigned long Unstore ( FILE *destination )
{
    unsigned long crc;
    unsigned int count;
    unsigned char buffer[ 256 ];
    int pacifier;

    pacifier = 0;
    crc = CRC_MASK;
    while ( Header.original_size != 0 ) {
        if ( Header.original_size > 256 )
            count = 256;
        else
            count = (int) Header.original_size;
	if ( fread( buffer, 1, count, InputCarFile ) != count )
	    FatalError( "Can't read from input CAR file" );
	if ( fwrite( buffer, 1, count, destination ) != count ) {
            fprintf( stderr, "Error writing to output file" );
	    return( ~Header.original_crc );
        }
        crc = CalculateBlockCRC32( count, crc, buffer );
	if ( destination != stdout && ( pacifier++ & 15 ) == 0 )
            putc( '.', stderr );
	Header.original_size -= count;
    }
    return( crc ^ CRC_MASK );
}



#define INDEX_BIT_COUNT      12
#define LENGTH_BIT_COUNT     4
#define WINDOW_SIZE          ( 1 << INDEX_BIT_COUNT )
#define RAW_LOOK_AHEAD_SIZE  ( 1 << LENGTH_BIT_COUNT )
#define BREAK_EVEN           ( ( 1 + INDEX_BIT_COUNT + LENGTH_BIT_COUNT ) / 9 )
#define LOOK_AHEAD_SIZE      ( RAW_LOOK_AHEAD_SIZE + BREAK_EVEN )
#define TREE_ROOT            WINDOW_SIZE
#define END_OF_STREAM        0
#define UNUSED               0
#define MOD_WINDOW( a )      ( ( a ) & ( WINDOW_SIZE - 1 ) )


unsigned char window[ WINDOW_SIZE ];

struct {
    int parent;
    int smaller_child;
    int larger_child;
} tree[ WINDOW_SIZE + 1 ];



void InitTree( int r );
void ContractNode( int old_node, int new_node );
void ReplaceNode( int old_node, int new_node );
int FindNextNode( int node );
void DeleteString( int p );
int AddString( int new_node, int *match_position );
void InitOutputBuffer( void );
int FlushOutputBuffer( void );
int OutputChar( int data );
int OutputPair( int position, int length );
void InitInputBuffer( void );
int InputBit( void );

void InitTree ( int r )
{
    int i;

    for ( i = 0 ; i < ( WINDOW_SIZE + 1 ) ; i++ ) {
        tree[ i ].parent = UNUSED;
        tree[ i ].larger_child = UNUSED;
        tree[ i ].smaller_child = UNUSED;
    }
    tree[ TREE_ROOT ].larger_child = r;
    tree[ r ].parent = TREE_ROOT;
    tree[ r ].larger_child = UNUSED;
    tree[ r ].smaller_child = UNUSED;
}

void ContractNode ( int old_node,  int new_node )
{
    tree[ new_node ].parent = tree[ old_node ].parent;
    if ( tree[ tree[ old_node ].parent ].larger_child == old_node )
        tree[ tree[ old_node ].parent ].larger_child = new_node;
    else
        tree[ tree[ old_node ].parent ].smaller_child = new_node;
    tree[ old_node ].parent = UNUSED;
}

void ReplaceNode ( int old_node,  int new_node )
{
    int parent;

    parent = tree[ old_node ].parent;
    if ( tree[ parent ].smaller_child == old_node )
        tree[ parent ].smaller_child = new_node;
    else
        tree[ parent ].larger_child = new_node;
    tree[ new_node ] = tree[ old_node ];
    tree[ tree[ new_node ].smaller_child ].parent = new_node;
    tree[ tree[ new_node ].larger_child ].parent = new_node;
    tree[ old_node ].parent = UNUSED;
}

int FindNextNode ( int node )
{
    int next;

    next = tree[ node ].smaller_child;
    while ( tree[ next ].larger_child != UNUSED )
        next = tree[ next ].larger_child;
    return( next );
}

void DeleteString ( int p )
{
    int  replacement;

    if ( tree[ p ].parent == UNUSED )
        return;
    if ( tree[ p ].larger_child == UNUSED )
        ContractNode( p, tree[ p ].smaller_child );
    else if ( tree[ p ].smaller_child == UNUSED )
        ContractNode( p, tree[ p ].larger_child );
    else {
        replacement = FindNextNode( p );
        DeleteString( replacement );
        ReplaceNode( p, replacement );
    }
}


int AddString ( int new_node,  int *match_position )
{
    int i;
    int test_node;
    int delta;
    int match_length;
    int *child;

    if ( new_node == END_OF_STREAM )
        return( 0 );
    test_node = tree[ TREE_ROOT ].larger_child;
    match_length = 0;
    for ( ; ; ) {
        for ( i = 0 ; i < LOOK_AHEAD_SIZE ; i++ ) {
            delta = window[ MOD_WINDOW( new_node + i ) ] -
                    window[ MOD_WINDOW( test_node + i ) ];
            if ( delta != 0 )
                break;
        }
        if ( i >= match_length ) {
            match_length = i;
            *match_position = test_node;
            if ( match_length >= LOOK_AHEAD_SIZE ) {
                ReplaceNode( test_node, new_node );
                return( match_length );
            }
        }
        if ( delta >= 0 )
            child = &tree[ test_node ].larger_child;
        else
            child = &tree[ test_node ].smaller_child;
        if ( *child == UNUSED ) {
            *child = new_node;
            tree[ new_node ].parent = test_node;
            tree[ new_node ].larger_child = UNUSED;
            tree[ new_node ].smaller_child = UNUSED;
            return( match_length );
        }
        test_node = *child;
    }
}

char DataBuffer[ 17 ];
int FlagBitMask;
unsigned int BufferOffset;

void InitOutputBuffer ()
{
    DataBuffer[ 0 ] = 0;
    FlagBitMask = 1;
    BufferOffset = 1;
}


int FlushOutputBuffer ()
{
    if ( BufferOffset == 1 )
        return( 1 );
    Header.compressed_size += BufferOffset;
    if ( ( Header.compressed_size ) >= Header.original_size )
        return( 0 );
    if ( fwrite( DataBuffer, 1, BufferOffset, OutputCarFile ) != BufferOffset )
        FatalError( "Error writing compressed data to CAR file" );
    InitOutputBuffer();
    return( 1 );
}


int OutputChar ( int data )
{
    DataBuffer[ BufferOffset++ ] = (char) data;
    DataBuffer[ 0 ] |= FlagBitMask;
    FlagBitMask <<= 1;
    if ( FlagBitMask == 0x100 )
        return( FlushOutputBuffer() );
    else
        return( 1 );
}


int OutputPair ( int position,  int length )
{
    DataBuffer[ BufferOffset ] = (char) ( length << 4 );
    DataBuffer[ BufferOffset++ ] |= ( position >> 8 );
    DataBuffer[ BufferOffset++ ] = (char) ( position & 0xff );
    FlagBitMask <<= 1;
    if ( FlagBitMask == 0x100 )
        return( FlushOutputBuffer() );
    else
        return( 1 );
}


void InitInputBuffer ()
{
    FlagBitMask = 1;
    DataBuffer[ 0 ] = (char) getc( InputCarFile );
}


int InputBit ()
{
    if ( FlagBitMask == 0x100 )
        InitInputBuffer();
    FlagBitMask <<= 1;
    return( DataBuffer[ 0 ] & ( FlagBitMask >> 1 ) );
}


int LZSSCompress ( FILE *input_text_file )
{
    int i;
    int c;
    int look_ahead_bytes;
    int current_position;
    int replace_count;
    int match_length;
    int match_position;

    Header.compressed_size = 0;
    Header.original_crc = CRC_MASK;
    InitOutputBuffer();

    current_position = 1;
    for ( i = 0 ; i < LOOK_AHEAD_SIZE ; i++ ) {
        if ( ( c = getc( input_text_file ) ) == EOF )
            break;
        window[ current_position + i ] = (unsigned char) c;
        Header.original_crc = UpdateCharacterCRC32( Header.original_crc, c );
    }
    look_ahead_bytes = i;
    InitTree( current_position );
    match_length = 0;
    match_position = 0;
    while ( look_ahead_bytes > 0 ) {
        if ( match_length > look_ahead_bytes )
            match_length = look_ahead_bytes;
        if ( match_length <= BREAK_EVEN ) {
            replace_count = 1;
            if ( !OutputChar( window[ current_position ] ) )
                return( 0 );
        } else {
            if ( !OutputPair( match_position, match_length - ( BREAK_EVEN + 1 ) ) )
                return( 0 );
            replace_count = match_length;
        }
        for ( i = 0 ; i < replace_count ; i++ ) {
            DeleteString( MOD_WINDOW( current_position + LOOK_AHEAD_SIZE ) );
            if ( ( c = getc( input_text_file ) ) == EOF ) {
                look_ahead_bytes--;
            } else {
                Header.original_crc =
                    UpdateCharacterCRC32( Header.original_crc, c );
                window[ MOD_WINDOW( current_position + LOOK_AHEAD_SIZE ) ] =
                    (unsigned char) c;
            }
            current_position = MOD_WINDOW( current_position + 1 );
            if ( current_position == 0 )
                putc( '.', stderr );
            if ( look_ahead_bytes )
                match_length = AddString( current_position, &match_position );
        }
    };
    Header.original_crc ^= CRC_MASK;
    return( FlushOutputBuffer() );
}


unsigned long LZSSExpand ( FILE *output )
{
    int i;
    int current_position;
    int c;
    int match_length;
    int match_position;
    unsigned long crc;
    unsigned long output_count;

    output_count = 0;
    crc = CRC_MASK;
    InitInputBuffer();
    current_position = 1;
    while ( output_count < Header.original_size ) {
        if ( InputBit() ) {
            c = getc( InputCarFile );
            putc( c, output );
            output_count++;
            crc = UpdateCharacterCRC32( crc, c );
            window[ current_position ] = (unsigned char) c;
            current_position = MOD_WINDOW( current_position + 1 );
            if ( current_position == 0 && output != stdout )
                putc( '.', stderr );
        } else {
            match_length = getc( InputCarFile );
            match_position = getc( InputCarFile );
            match_position |= ( match_length & 0xf ) << 8;
            match_length >>= 4;
            match_length += BREAK_EVEN;
            output_count += match_length + 1;
            for ( i = 0 ; i <= match_length ; i++ ) {
                c = window[ MOD_WINDOW( match_position + i ) ];
                putc( c, output );
                crc = UpdateCharacterCRC32( crc, c );
                window[ current_position ] = (unsigned char) c;
                current_position = MOD_WINDOW( current_position + 1 );
                if ( current_position == 0 && output != stdout )
                    putc( '.', stderr );
            }
        }
    }
    return( crc ^ CRC_MASK );
}
