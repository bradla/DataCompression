#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

// Platform-specific includes
#ifdef _WIN32
#include <windows.h>
#include <psapi.h>
#else
#include <unistd.h>
#include <sys/resource.h>
#include <sys/time.h>
#endif

// Structure to hold function performance data
typedef struct {
    const char *name;
    long call_count;
    double total_wall_time;
    double total_cpu_time;
    double total_user_time;
    double total_system_time;
    size_t peak_memory;
} FunctionProfile;

// Global profile data
#define MAX_FUNCTIONS 100
static FunctionProfile profiles[MAX_FUNCTIONS];
static int profile_count = 0;

// Structure for tracking active measurements
typedef struct {
    double start_wall;
    double start_user;
    double start_system;
    size_t start_memory;
    FunctionProfile *profile;
} ProfileContext;

// Stack for nested function calls
#define MAX_DEPTH 20
static ProfileContext call_stack[MAX_DEPTH];
static int stack_top = -1;

// Function to get current wall time in milliseconds
double get_wall_time() {
#ifdef _WIN32
    LARGE_INTEGER frequency, counter;
    QueryPerformanceFrequency(&frequency);
    QueryPerformanceCounter(&counter);
    return (double)counter.QuadPart * 1000.0 / (double)frequency.QuadPart;
#else
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (double)ts.tv_sec * 1000.0 + (double)ts.tv_nsec / 1000000.0;
#endif
}

// Function to get CPU times (user and system) in milliseconds
void get_cpu_times(double *user_time, double *system_time) {
#ifdef _WIN32
    FILETIME creation_time, exit_time, kernel_time, user_time_ft;
    GetProcessTimes(GetCurrentProcess(), &creation_time, &exit_time, 
                   &kernel_time, &user_time_ft);
    
    ULARGE_INTEGER kernel_uli, user_uli;
    kernel_uli.LowPart = kernel_time.dwLowDateTime;
    kernel_uli.HighPart = kernel_time.dwHighDateTime;
    user_uli.LowPart = user_time_ft.dwLowDateTime;
    user_uli.HighPart = user_time_ft.dwHighDateTime;
    
    *user_time = (double)user_uli.QuadPart * 0.0001; // Convert 100ns to ms
    *system_time = (double)kernel_uli.QuadPart * 0.0001;
#else
    struct rusage usage;
    getrusage(RUSAGE_SELF, &usage);
    *user_time = (double)usage.ru_utime.tv_sec * 1000.0 + 
                 (double)usage.ru_utime.tv_usec / 1000.0;
    *system_time = (double)usage.ru_stime.tv_sec * 1000.0 + 
                   (double)usage.ru_stime.tv_usec / 1000.0;
#endif
}

// Function to get memory usage in KB
size_t get_memory_usage() {
#ifdef _WIN32
    PROCESS_MEMORY_COUNTERS pmc;
    GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof(pmc));
    return pmc.WorkingSetSize / 1024;
#else
    struct rusage usage;
    getrusage(RUSAGE_SELF, &usage);
    return usage.ru_maxrss; // Returns KB on Linux
#endif
}

// Initialize profiling for a function
void profile_start(const char *func_name) {
    if (stack_top >= MAX_DEPTH - 1) {
        fprintf(stderr, "Profile stack overflow for function %s\n", func_name);
        return;
    }

    // Find or create profile for this function
    FunctionProfile *profile = NULL;
    for (int i = 0; i < profile_count; i++) {
        if (strcmp(profiles[i].name, func_name) == 0) {
            profile = &profiles[i];
            break;
        }
    }

    if (!profile) {
        if (profile_count >= MAX_FUNCTIONS) {
            fprintf(stderr, "Maximum number of profiled functions reached\n");
            return;
        }
        profile = &profiles[profile_count++];
        profile->name = func_name;
        profile->call_count = 0;
        profile->total_wall_time = 0;
        profile->total_cpu_time = 0;
        profile->total_user_time = 0;
        profile->total_system_time = 0;
        profile->peak_memory = 0;
    }

    // Push to call stack
    stack_top++;
    call_stack[stack_top].profile = profile;
    call_stack[stack_top].start_wall = get_wall_time();
    get_cpu_times(&call_stack[stack_top].start_user, 
                 &call_stack[stack_top].start_system);
    call_stack[stack_top].start_memory = get_memory_usage();
}

// End profiling for a function
void profile_end() {
    if (stack_top < 0) {
        fprintf(stderr, "Profile stack underflow\n");
        return;
    }

    ProfileContext *ctx = &call_stack[stack_top];
    FunctionProfile *profile = ctx->profile;

    double end_wall = get_wall_time();
    double end_user, end_system;
    get_cpu_times(&end_user, &end_system);
    size_t end_memory = get_memory_usage();

    // Update profile statistics
    profile->call_count++;
    profile->total_wall_time += end_wall - ctx->start_wall;
    profile->total_user_time += end_user - ctx->start_user;
    profile->total_system_time += end_system - ctx->start_system;
    profile->total_cpu_time = profile->total_user_time + profile->total_system_time;
    
    size_t memory_used = end_memory - ctx->start_memory;
    if (memory_used > profile->peak_memory) {
        profile->peak_memory = memory_used;
    }

    stack_top--;
}

// Print all profile results
void print_profiles() {
    printf("\nFunction Performance Profile:\n");
    printf("----------------------------------------------------------------------------\n");
    printf("%-20s %8s %12s %12s %12s %12s %12s\n", 
           "Function", "Calls", "Wall(ms)", "CPU(ms)", "User(ms)", "System(ms)", "Mem(KB)");
    printf("----------------------------------------------------------------------------\n");

    for (int i = 0; i < profile_count; i++) {
        FunctionProfile *p = &profiles[i];
        printf("%-20s %8ld %12.2f %12.2f %12.2f %12.2f %12zu\n",
               p->name, p->call_count, p->total_wall_time, p->total_cpu_time,
               p->total_user_time, p->total_system_time, p->peak_memory);
    }
}