# GamingCenter dev tasks
# Usage: make [target]
# Works in WSL and Windows (via wsl make or Git Bash make)

DOTNET  := $(shell command -v dotnet 2>/dev/null || echo /mnt/c/Program\ Files/dotnet/dotnet.exe)
CURL    := $(shell command -v curl 2>/dev/null || echo /mnt/c/Windows/System32/curl.exe)
SLN     := GamingCenter.Replacement.slnx
SERVER  := src/GamingCenter.Server
PORT    ?= 5055
BASE    := http://127.0.0.1:$(PORT)

UI      := src/GamingCenter.UI
.PHONY: run watch test build fan ec system write publish clean help

help: ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-10s\033[0m %s\n", $$1, $$2}'

run: build ## Build and start the server
	@echo "Starting on port $(PORT)..."
	@"$(DOTNET)" run --project $(SERVER) --no-build -- $(PORT)

watch: ## Hot-reload mode
	@"$(DOTNET)" watch run --project $(SERVER) -- $(PORT)

build: ## Build the solution
	@"$(DOTNET)" build $(SLN) --nologo --verbosity quiet

test: ## Run all tests
	@"$(DOTNET)" test $(SLN) --nologo --verbosity normal

fan: ## Read current fan mode
	@"$(CURL)" -s $(BASE)/api/fan/mode && echo

ec: ## Read EC diagnostic snapshot
	@"$(CURL)" -s $(BASE)/api/fan/diagnostic && echo

system: ## Read system snapshot
	@"$(CURL)" -s $(BASE)/api/system/snapshot | head -c 500 && echo

write: ## Write fan mode: make write MODE=boost
	@if [ -z "$(MODE)" ]; then echo "Usage: make write MODE=[auto|boost|custom|L1..L5]"; exit 1; fi
	@"$(CURL)" -s -X POST $(BASE)/api/fan/mode \
		-H "Content-Type: application/json" \
		-d '{"mode":"$(MODE)"}' && echo

clean: ## Remove build artifacts
	@"$(DOTNET)" clean $(SLN) --nologo 2>/dev/null; \
	find . -type d \( -name bin -o -name obj \) -exec rm -rf {} + 2>/dev/null; \
	echo "Cleaned."

publish: ## Build single-file self-contained .exe
	@echo "Publishing GamingCenter.exe (single-file, self-contained)..."
	@"$(DOTNET)" publish $(UI) -c Release -r win-x64 --nologo
	@echo ""
	@echo "Output: $(UI)/bin/Release/net10.0-windows/win-x64/publish/GamingCenter.UI.exe"
