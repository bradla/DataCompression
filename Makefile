DOTNET = dotnet
ARITHC_PROJ = csfiles/Arith-c.csproj
ARITHE_PROJ = csfiles/Arith-e.csproj
ARITH1C_PROJ = csfiles/Arith1-c.csproj
ARITH1E_PROJ = csfiles/Arith1-e.csproj
ARITH1eC_PROJ = csfiles/Arith1e-c.csproj
ARITH1eE_PROJ = csfiles/Arith1e-e.csproj
LZW12C_PROJ = csfiles/Lzw12-c.csproj
LZW12E_PROJ = csfiles/Lzw12-e.csproj
LZW15VC_PROJ = csfiles/Lzw15v-c.csproj
LZW15VE_PROJ = csfiles/Lzw15v-e.csproj
LZSSC_PROJ = csfiles/Lzss-c.csproj
LZSSE_PROJ = csfiles/Lzss-e.csproj

CARMAN_PROJ = csfiles/Carman.csproj
CHURN_PROJ = csfiles/Churn.csproj

# Common output directories (moved out of csfiles/)
BIN_DIR = build/bin
OBJ_DIR = build/obj

# -------------------------
# Default target
# -------------------------
all: Carman Churn Arith Lzw15v Lzw12

# -------------------------
# Build Targets
# -------------------------
Arith:
	$(DOTNET) build $(ARITHC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(ARITHE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Arith1:
	$(DOTNET) build $(ARITH1C_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(ARITH1E_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Arith1e:
	$(DOTNET) build $(ARITH1eC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(ARITH1eE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Lzw15v:
	$(DOTNET) build $(LZW15VC_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(LZW15VE_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Lzw12:
	$(DOTNET) build $(LZW12C_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
	$(DOTNET) build $(LZW12E_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Churn:
	$(DOTNET) build $(CHURN_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
Carman:
	$(DOTNET) build $(CARMAN_PROJ) -c Release -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/

# -------------------------
# Run Targets
# -------------------------
#build-debug:
#	$(DOTNET) build $(C_PROJ) -c Debug -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/
#	$(DOTNET) build $(E_PROJ) -c Debug -p:BaseOutputPath=$(BIN_DIR)/ -p:BaseIntermediateOutputPath=$(OBJ_DIR)/

run-compress-release:
	$(DOTNET) run --project $(C_PROJ) -c Release --property:BaseOutputPath=$(BIN_DIR)/ --property:BaseIntermediateOutputPath=$(OBJ_DIR)/

run-extract-release:
	$(DOTNET) run --project $(E_PROJ) -c Release --property:BaseOutputPath=$(BIN_DIR)/ --property:BaseIntermediateOutputPath=$(OBJ_DIR)/

run-compress-debug:
	$(DOTNET) run --project $(C_PROJ) -c Debug --property:BaseOutputPath=$(BIN_DIR)/ --property:BaseIntermediateOutputPath=$(OBJ_DIR)/

run-extract-debug:
	$(DOTNET) run --project $(E_PROJ) -c Debug --property:BaseOutputPath=$(BIN_DIR)/ --property:BaseIntermediateOutputPath=$(OBJ_DIR)/

# -------------------------
# Clean Target
# -------------------------
clean:
	$(DOTNET) clean $(C_PROJ)
	$(DOTNET) clean $(E_PROJ)
	rm -rf $(BIN_DIR) $(OBJ_DIR)

# List available targets
list:
	@echo "Available targets:"
	@echo "  all              - Build everything"
	@echo "  Carman           - Build Carman.cs"
	@echo "  Arith            - Build Arith.cs"
	@echo "  Arith1           - Build Arith1.cs"
	@echo "  Arith1e          - Build Arith1e.cs"
	@echo "  Lzw12            - Build Lzw12.cs"
	@echo "  Lzw15v           - Build Arith1.cs"
	@echo "  Lzss             - Build Lzss.cs"
	@echo "  Churn            - Build Churn.cs"
	@echo "  run-carman       - Run Carman project"
	@echo "  run-*            - Run individual programs"
	@echo "  clean            - Clean all build artifacts"


.PHONY: all clean list run-carman run-churn run-arith-c run-arith-e
