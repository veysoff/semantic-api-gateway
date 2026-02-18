#!/bin/bash
# Semantic API Gateway - Start All Services
# Prerequisites: .NET 10.0 SDK, Bash 4.0+

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
NOWAIT=false; HELP=false
while [[ $# -gt 0 ]]; do case $1 in --no-wait) NOWAIT=true; shift ;; --help) HELP=true; shift ;; *) echo "Unknown option: $1"; HELP=true; shift ;; esac; done
if [ "$HELP" = true ]; then cat << 'EOF'
Semantic API Gateway - Start All Services
USAGE: ./start-all.sh [OPTIONS]
OPTIONS: --no-wait (don't wait), --help (this message)
EXAMPLE: ./start-all.sh
Services (ports): Gateway 5000, Order 5100, User 5300, Inventory 5200
NEXT STEPS: 1. Open http://localhost:5000/swagger  2. Import Postman collection
EOF
exit 0; fi
if ! command -v dotnet &> /dev/null; then echo -e "${RED}✗ ERROR: .NET SDK not found${NC}"; exit 1; fi
DOTNET_VERSION=$(dotnet --version); echo -e "${GREEN}✓ .NET SDK: $DOTNET_VERSION${NC}"
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
declare -A services; services[Gateway]="SemanticApiGateway.Gateway:5000"; services[Order]="SemanticApiGateway.MockServices/OrderService:5100"; services[User]="SemanticApiGateway.MockServices/UserService:5300"; services[Inventory]="SemanticApiGateway.MockServices/InventoryService:5200"
for svc in "${!services[@]}"; do IFS=':' read -r path port <<< "${services[$svc]}"; if [ ! -d "$SCRIPT_DIR/$path" ]; then echo -e "${RED}✗ ERROR: $svc not found at $path${NC}"; exit 1; fi; done
echo -e "${GREEN}✓ All service directories verified${NC}"; echo ""
for svc in "${!services[@]}"; do IFS=':' read -r path port <<< "${services[$svc]}"; echo -e "${CYAN}Starting $svc (port $port)...${NC}"; svc_path="$SCRIPT_DIR/$path"
if [ "$(uname)" == "Darwin" ]; then osascript <<OSASCRIPT
tell application "Terminal"
    do script "cd '$svc_path' && dotnet run"
end tell
OSASCRIPT
else if command -v gnome-terminal &> /dev/null; then gnome-terminal -- bash -c "cd '$svc_path' && dotnet run; bash" &
elif command -v xterm &> /dev/null; then xterm -e "cd '$svc_path' && dotnet run" &
elif command -v konsole &> /dev/null; then konsole -e bash -c "cd '$svc_path' && dotnet run" &
else (cd "$svc_path" && dotnet run) & fi; fi; done
echo -e "${GREEN}✓ All services started${NC}"; sleep 3
if [ "$NOWAIT" = false ]; then echo -e "${YELLOW}Waiting for Gateway...${NC}"; for i in {1..30}; do curl -s http://localhost:5000/health > /dev/null 2>&1 && echo -e "${GREEN}✓ Gateway ready!${NC}" && break; sleep 1; done; fi
echo ""; echo -e "${GREEN}Services running:${NC}"; for svc in "${!services[@]}"; do IFS=':' read -r path port <<< "${services[$svc]}"; echo -e "  ${GREEN}•${NC} $svc: http://localhost:$port"; done
echo -e "  ${GREEN}•${NC} Swagger: http://localhost:5000/swagger"; echo ""; echo -e "${GREEN}Visit http://localhost:5000/swagger${NC}"
