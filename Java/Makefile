# Simple Makefile for LZW12 with mainc.java, lzw12.java, bitio.java
JC = javac
JAVA = java
SRCS = mainc.java lzw12.java bitio.java
CLASSES = $(SRCS:.java=.class)

# Default target
all: $(CLASSES)

# Compile rule
%.class: %.java
	$(JC) $<

# Run compression
run: all
	@if [ "$(INPUT)" = "" ] || [ "$(OUTPUT)" = "" ]; then \
		echo "Usage: make run INPUT=input.txt OUTPUT=output.lzw"; \
		echo "Example: make run INPUT=test.txt OUTPUT=test.lzw"; \
	else \
		echo "Compressing $(INPUT) to $(OUTPUT)..."; \
		$(JAVA) mainc $(INPUT) $(OUTPUT); \
	fi

# Quick test
test: all
	@echo "Creating test file..."
	@echo "Hello World! This is a test for LZW compression." > test.txt
	@echo "Repeated patterns are good for compression." >> test.txt
	@echo "Hello World! This is a test for LZW compression." >> test.txt
	@echo "" >> test.txt
	@echo "File size:"
	@wc -c test.txt
	@echo ""
	@echo "Compressing test.txt to test.lzw..."
	@$(JAVA) mainc test.txt test.lzw
	@echo ""
	@echo "Compressed file size:"
	@wc -c test.lzw
	@echo ""
	@echo "Test completed."

# Clean up
clean:
	rm -f *.class
	rm -f test.txt test.lzw
	@echo "Cleaned up."

# Help
help:
	@echo "LZW12 Makefile"
	@echo "=============="
	@echo "Files: mainc.java, lzw12.java, bitio.java"
	@echo ""
	@echo "Commands:"
	@echo "  make              - Compile all Java files"
	@echo "  make run          - Run LZW compression (set INPUT and OUTPUT)"
	@echo "  make test         - Run a quick compression test"
	@echo "  make clean        - Remove compiled files"
	@echo "  make help         - Show this help"
	@echo ""
	@echo "Example:"
	@echo "  make"
	@echo "  make run INPUT=file.txt OUTPUT=file.lzw"
	@echo "  java mainc input.txt output.lzw"

.PHONY: all run test clean help
