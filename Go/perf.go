package main

import (
	"fmt"
	"runtime"
	"syscall"
	"time"
)

func getCPUTime() time.Duration {
	var creation, exit, kernel, user syscall.Filetime
	h := syscall.Handle(^uintptr(0)) // current process
	syscall.GetProcessTimes(h, &creation, &exit, &kernel, &user)

	k := filetimeToDuration(kernel)
	u := filetimeToDuration(user)
	return k + u
}

func filetimeToDuration(ft syscall.Filetime) time.Duration {
	return time.Duration(ft.HighDateTime)<<32 + time.Duration(ft.LowDateTime)
}

var headerPrinted = false

func Track(name string, fn func()) {
	start := time.Now()

	var memStart runtime.MemStats
	runtime.ReadMemStats(&memStart)

	fn()

	duration := time.Since(start)

	var memEnd runtime.MemStats
	runtime.ReadMemStats(&memEnd)

	memUsedKB := float64(memEnd.Alloc-memStart.Alloc) / 1024

	if !headerPrinted {
		fmt.Printf("%-15s %10s %10s %8s %12s\n", "Name", "Wall (ms)", "CPU (ms)", "CPU (%)", "Memory (KB)")
		headerPrinted = true
	}

	fmt.Printf("%-15s %10.2f %10s %8s %12.2f\n",
		name,
		float64(duration.Milliseconds()),
		"N/A", // placeholder for CPU (ms)
		"N/A", // placeholder for CPU (%)
		memUsedKB,
	)
}

func TrackWithResult[T any](name string, fn func() T) T {
	start := time.Now()

	var memStart runtime.MemStats
	runtime.ReadMemStats(&memStart)

	//startCPU := getCPUTime()

	result := fn()

	//endCPU := getCPUTime()
	duration := time.Since(start)

	var memEnd runtime.MemStats
	runtime.ReadMemStats(&memEnd)

	memUsedKB := float64(memEnd.Alloc-memStart.Alloc) / 1024
	//cpuTime := endCPU.Sub(startCPU)
	//cpuPct := float64(cpuTime.Microseconds()) / float64(duration.Microseconds()*int64(runtime.NumCPU())) * 100

	if !headerPrinted {
		fmt.Printf("%-15s %10s %10s %8s %12s\n", "Name", "Wall (ms)", "CPU (ms)", "CPU (%)", "Memory (KB)")
		headerPrinted = true
	}

	fmt.Printf("%-15s %10.2f %10.2f %8.2f %12.2f\n",
		name,
		float64(duration.Milliseconds()),
		0.0, // placeholder for CPU (ms)
		0.0, // placeholder for CPU (%)
		memUsedKB,
	)

	return result
}

func TrackWithResult2[T1 any, T2 any](name string, fn func() (T1, T2)) (T1, T2) {
	start := time.Now()

	var memStart runtime.MemStats
	runtime.ReadMemStats(&memStart)

	//startCPU := getCPUTime()

	v1, v2 := fn()

	//endCPU := getCPUTime()
	duration := time.Since(start)

	var memEnd runtime.MemStats
	runtime.ReadMemStats(&memEnd)

	memUsedKB := float64(memEnd.Alloc-memStart.Alloc) / 1024
	//cpuTime := endCPU.Sub(startCPU)
	//cpuPct := float64(cpuTime.Microseconds()) / float64(duration.Microseconds()*int64(runtime.NumCPU())) * 100

	if !headerPrinted {
		fmt.Printf("%-15s %10s %10s %8s %12s\n", "Name", "Wall (ms)", "CPU (ms)", "CPU (%)", "Memory (KB)")
		headerPrinted = true
	}

	fmt.Printf("%-15s %10.2f %10.2f %8.2f %12.2f\n",
		name,
		float64(duration.Milliseconds()),
		0.0, // placeholder for CPU (ms)
		0.0, // placeholder for CPU (%)
		memUsedKB,
	)

	return v1, v2
}
