APP := lzw12.exe
SRC := bitio.go main-c.go perf.go lzw12.go

build:
		go build -o $(APP) $(SRC)

run: build
		./$(APP)

test:
	        lzw12.exe test.txt test.out	
debug:
		test.txt test.out

clean:
		rm -f $(APP)
