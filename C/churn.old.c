/***************************START OF CHURN.C***************************
 *
 * This is a utility program used to test compression/decompression
 * programs for accuracy, speed, and compression ratios.  CHURN is
 * called with three arguments.  The first is a root directory.  CHURN
 * will attempt to compress and then decompress every file in and under
 * the specified root directory.  The next parameter on the command
 * line is the compression command.  CHURN needs to compress the input
 * file to a file called TEST.CMP.  The compression command tells CHURN
 * how to to do this.  CHURN will execute the compression command by
 * passing the command line to DOS using the system() function call.
 * It attempts to insert the file name into the compression command by
 * calling sprintf(), with the file name as an argument.  This means that
 * if the compression command has a %s anywhere in it, the name of the
 * input file should be substituted for it.  Finally, the third argument
 * on the command line should be the command CHURN needs to spawn to
 * decompress TEST.CMP to TEST.OUT.
 *
 * An example of how this works using programs created in this book
 * would look like this:
 *
 *  CHURN C:\ "LZSS-C %%s test.cmp" "LZSS-E test.cmp test.out"
 *
 * The doubled up % symbols are there to defeat variable substitution
 * under some command line interpreters, such as 4DOS.
 *
 * A more complicated example testing PKZIP might look like this:
 *
 * CHURN C:\ "TEST %%s" "PKUNZIP TEST.CMP"
 *
 * where TEST.BAT had two lines that look like this:
 *
 *     COPY %1 TEST.OUT
 *     PKZIP -M TEST.CMP TEST.OUT
 *
 * CHURN stores a summary of compression in a file called CHURN.LOG.  This
 * file could be used for further analysis by other programs.
 *
 * To abort this program while it is running, don't start pounding away on
 * the BREAK or CTRL-C keys.  They will just get absorbed by the compression
 * program.  Instead, hit a single key, which will be detected by CHURN, and
 * used as an abort signal.
 */
#define MSDOS 1
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <process.h>
#include <conio.h>
#include <dos.h>

/*
 * The findfirst and findnext functions operate nearly identically
 * under TurboC and MSC.  The only difference is that the functions
 * names, structures, and structure elements all have different names.
 * I just create macros for these things and redefine them appropriately
 * here.
 */

#ifdef __TURBOC__

#include <dir.h>
#define FILE_INFO                 struct ffblk
#define FIND_FIRST( name, info )  findfirst( ( name ), ( info ), FA_DIREC )
#define FIND_NEXT( info )         findnext( ( info ) )
#define FILE_IS_DIR( info )       ( ( info ).ff_attrib & FA_DIREC )
#define FILE_NAME( info )         ( ( info ).ff_name )

#else

#define MSDOS 1
#define FILE_INFO                 struct find_t
#define FIND_FIRST( name, info )  _dos_findfirst( ( name ), _A_SUBDIR, ( info ) )
#define FIND_NEXT( info )         _dos_findnext( ( info ) )
#define FILE_IS_DIR( info )       ( ( info ).attrib & _A_SUBDIR )
#define FILE_NAME( info )         ( ( info ).name )

#endif

/*
 * Some global variables.
 */

int total_files;
int total_passed;
int total_failed;
char *compress_command;
char *expand_command;
FILE *input;
FILE *output;
FILE *compressed;
FILE *log_file;

/*
 * Declarations for global routines.
 */

void churn_files( char *path );
int file_is_already_compressed( char *name );
void close_all_the_files( void );
int compress( char *file_name );
void usage_exit( void );


/*
 * main() doesn't have to do a whole lot in this program.  It
 * reads in the command line to determine what the root directory
 * to start looking at is, then it initializes the total byte counts
 * and the start time.  It can then call churn_files(), which does all
 * the work, then report on the statistics resulting from churn_files.
 */

void main( int argc, char *argv[] )
{
    time_t start_time;
    time_t stop_time;
    char root_dir[ 81 ];

    if ( argc != 4 )
        usage_exit();
    strcpy( root_dir, argv[ 1 ] );
    if ( root_dir[ strlen( root_dir ) - 1 ] != '\\' )
       strcat( root_dir, "\\" );
    compress_command = argv[ 2 ];
    expand_command = argv[ 3 ];
    setbuf( stdout, NULL );
    setbuf( stderr, NULL );
    total_files = 0;
    total_passed = 0;
    total_failed = 0;
    log_file = fopen( "CHURN.LOG", "w" );
    if ( log_file == NULL ) {
        printf( "Couldn't open the log file!\n" );
        exit( 1 );
    }
    fprintf( log_file, "                                          "
            "Original   Packed\n" );
    fprintf( log_file, "            File Name                     "
            "  Size      Size   Ratio  Result\n" );
    fprintf( log_file, "-------------------------------------     "
            "--------  --------  ----  ------\n" );
    time( &start_time );
    churn_files( root_dir );
    time( &stop_time );
    fprintf( log_file, "\nTotal elapsed time: %f seconds\n",
             difftime( stop_time, start_time ) );
    fprintf( log_file, "Total files:   %d\n", total_files );
    fprintf( log_file, "Total passed:  %d\n", total_passed );
    fprintf( log_file, "Total failed:  %d\n", total_failed );
}

/*
 * churn_files() is a routine that sits in a loop looking at
 * files in the directory specified by its single argument, "path".
 * As each file is looked at, one of three things happens.  If it
 * is a normal file, and has a compressed extension name, like ".ZIP",
 * the file is ignored.  If it is a normal file, and doesn't have a
 * compressed extension name, it is compressed and decompressed by
 * another routine.  Finally, if the file is a subdirectory,
 * churn_files() is called recursively with the file name as its
 * path argument.  This is one of those rare routines where recursion
 * provides a way to truly simplify the task at hand.
 */

void churn_files( char *path )
{
    FILE_INFO file_info;
    int result;
    char full_name[ 81 ];

    strcpy( full_name, path );
    strcat( full_name, "*.*" );
    result = FIND_FIRST( full_name, &file_info );

    while ( result == 0 ) {
        if ( kbhit() ) {
            getch();
            exit(0);
        }
        if ( FILE_IS_DIR( file_info ) ) {
            if ( FILE_NAME( file_info )[ 0 ] != '.' ) {
                strcpy( full_name, path );
                strcat( full_name, FILE_NAME( file_info) );
                strcat( full_name, "\\" );
                churn_files( full_name );
            }
        } else {
            strcpy( full_name, path );
            strcat( full_name, FILE_NAME( file_info )  );
            if ( !file_is_already_compressed( full_name ) ) {
                fprintf( stderr, "Testing %s\n", full_name );
                if ( !compress( full_name ) )
                    fprintf( stderr, "Comparison failed!\n" );
            }
        }
        result = FIND_NEXT( &file_info );
    }
}


/*
 * The job of this routine is simply to check on the file
 * whose name is passed as an argument.  The file extension is compared
 * agains a list of standard extensions that are commonly used on
 * compressed files.  If it matches one of these names, we assume it is
 * compressed and return a TRUE, otherwise FALSE is returned.
 *
 * Note that when checking a compression routine for accuracy, it is
 * probably a good idea to stub out this routine.  Trying to compress
 * "incompressible" files is a very good exercise for a compression
 * program.  It is probably not a good idea when checking compression
 * ratios, however.
 */

int file_is_already_compressed( char *name )
{
    char *extension;
    static char *matches[]={ "ZIP", "ICE", "LZH", "ARC", "GIF", "PAK",
                             "ARJ", NULL };
    int i;

    extension=strchr( name, '.' );
    if ( extension++ == NULL )
        return( 0 );
    i = 0;
    while ( matches[ i ] != NULL )
        if ( strcmp( extension, matches[ i++ ] ) == 0 )
            return( 1 );
    return( 0 );
}


/*
 * This is the routine that does the majority of the work for
 * this program.  It takes a file whose name is passed here.  It first
 * compresses, then decompresses that file.  It then compares the file
 * to the decompressed output, and reports on the results.
 */

int compress( char *file_name )
{
    long new_size;
    long old_size;
    int c;
    char command[ 132 ];

    printf( "%s\n", file_name );
    fprintf( log_file, "%-40s ", file_name );
    sprintf( command, compress_command, file_name );
    system( command );
    sprintf( command, expand_command, file_name );
    system( command );

    input = fopen( file_name, "rb" );
    output = fopen( "TEST.OUT", "rb" );
    compressed = fopen( "TEST.CMP", "rb" );

    total_files++;
    if ( input == NULL || output == NULL || compressed == NULL ) {
        total_failed++;
        close_all_the_files();
        fprintf( log_file, "Failed, couldn't open file!\n" );
        return( 0 );
    }

    fseek( input, 0L, SEEK_END );
    old_size = ftell( input );
    fseek( input, 0L, SEEK_SET );
    fseek( compressed, 0L, SEEK_END );
    new_size = ftell( compressed );

    fprintf( log_file, " %8ld  %8ld ", old_size, new_size );
    if ( old_size == 0L )
        old_size = 1L;
    fprintf( log_file, "%4ld%%  ",
            100L - ( ( 100L * new_size ) / old_size ) );
    do {
        c = getc( input );
        if ( getc( output ) != c ) {
            fprintf( log_file, "Failed\n" );
            total_failed++;
            close_all_the_files();
            return( 0 );
        }
    }
    while ( c != EOF );
    fprintf( log_file, "Passed\n" );
    close_all_the_files();
    total_passed++;
    return( 1 );
}

void close_all_the_files()
{
    if ( input != NULL )
        fclose( input );
    if ( output != NULL )
        fclose( output );
    if ( compressed != NULL )
        fclose( compressed );
}

/*
 * This routine is used to print out basic instructions for the use
 * of CHURN, and then exit.
 */

void usage_exit( void )
{
    char *usage = "CHURN 1.0. Usage:  CHURN root-dir \"compress "
                  "command\" \"expand command\n"
                  "\n"
                  "CHURN is used to test compression programs.  "
                  "It does this by compressing and\n"
                  "then expanding all of the files in and under "
                  "the specified root dir.\n"
                  "\n"
                  "For each file it finds, CHURN first executes "
                  "the compress command to create a\n"
                  "compressed file called TEST.CMP.  It then "
                  "executes the expand command to\n"
                  "create a file called TEST.OUT. CHURN then "
                  "compares the two files to make sure\n"
                  "the compression cycle worked properly.\n"
                  "\n"
                  "The file name to be compressed will be "
                  "inserted into the compress command\n"
                  "using sprintf, with any %%s argument being "
                  "substituted with the name of the\n"
                  "file being compressed.  Note that the "
                  "compress and expand commands should be\n"
                  "enclosed in double quotes so that multiple "
                  "words can be included in the\n"
                  "commands.\n"
                  "\n"
                  "Note that you may have to double the %% "
                  "character on your command line to get\n"
                  "around argument substitution under some "
                  "command processors. Finally, note that\n"
                  "CHURN executes the compression program "
                  "using a system() function call, so\n"
                  "batch files can be used to execute complex "
                  "compression sequences.\n"
                  "\n"
                  "Example:  CHURN C:\\ \"LZSS-C %%s TEST.CMP\""
                  " \"LZSS-C TEST.CMP TEST.OUT\"";

    puts( usage );
    exit( 1 );
}

/***************************END OF CHURN.C***************************/

