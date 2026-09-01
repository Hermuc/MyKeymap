version = 2.0-beta33
ahkVersion = 2.0.19
folder = MyKeymap-$(version)
zip = $(folder).7z

buildServer:
	go.exe build -C ./config-server -tags=nomsgpack -ldflags "-s -w -X settings/internal/script.MykeymapVersion=$(version)" -o ../bin/settings.exe ./cmd/settings
	rm -f -r bin/templates
	cp -r config-server/templates bin/templates

# 发布 Avalonia 原生设置界面到 bin/ui/ (自包含, 免装 .NET 运行时)
# 注意 PATH 陷阱: C:\Program Files\dotnet 可能只有运行时没有 SDK, 须显式探测
buildClientAvalonia:
	@dotnet --list-sdks | grep -q . || (echo "[错误] dotnet --list-sdks 为空: 未找到 .NET SDK (PATH 陷阱: C:\\Program Files\\dotnet 可能只有运行时无 SDK), 请安装 SDK 或将 PATH 指向含 SDK 的 dotnet.exe"; exit 1)
	rm -f -r bin/ui
	cd config-ui-avalonia; dotnet publish -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:SatelliteResourceLanguages="zh-Hans;en" -o ../bin/ui

copyFiles: CopyAHK
	rm -f -r $(folder)
	mkdir $(folder)
	mkdir $(folder)/shortcuts

	rm -f -r bin/site
	cp -r site-assets bin/site

	cp -r data $(folder)/
	cp -r bin $(folder)/
	cp -r tools $(folder)/
	rm -f $(folder)/tools/oracle.ps1
	cp MyKeymap.exe $(folder)/
	cp 误报病毒时执行这个.bat $(folder)/

# 如果直接用 wsl 的 cp 命令复制, 复制出的文件会有 read-only 属性, 比较奇怪
CopyAHK:
	@echo '@copy /y "C:\\Program Files\\AutoHotkey\\v2\AutoHotkey64.exe" .\\bin\\' > CopyAHK.bat
	cmd.exe /c CopyAHK.bat
	rm CopyAHK.bat

build: buildServer buildClientAvalonia copyFiles
	cd bin; ./settings.exe ChangeVersion $(version)
	rm -f MyKeymap-*.7z
	7z.exe a $(zip) $(folder)
	rm -f -r $(folder)
	@echo ------------------------- build ok -------------------------------

createRelease:
	curl -L \
		-X POST \
		-H "Accept: application/vnd.github+json" \
		-H "Authorization: Bearer $$(cat ~/gh_token)" \
		-H "X-GitHub-Api-Version: 2022-11-28" \
		https://api.github.com/repos/xianyukang/MyKeymap/releases \
		-d '{"tag_name":"v$(version)","target_commitish":"main","name":"v$(version)","body":"Description of the release"}' 2>/dev/null | jq -r '.id' > release_id
	curl -L \
		-X POST \
		-H "Accept: application/vnd.github+json" \
		-H "Authorization: Bearer $$(cat ~/gh_token)" \
		-H "X-GitHub-Api-Version: 2022-11-28" \
		-H "Content-Type: application/octet-stream" \
		"https://uploads.github.com/repos/xianyukang/MyKeymap/releases/$$(cat release_id)/assets?name=$(zip)" \
		--data-binary "@$(zip)" | jq
	rm release_id


uploadLanZou:
	go run build_tools.go checkForAHKUpdate $(ahkVersion)
	python3 lanzou_client.py $(zip) 2> share_link.json
	go run build_tools.go updateShareLink $(version)
	rm -f share_link.json

upload: uploadLanZou createRelease
	@echo ------------------------- upload ok -------------------------------

# 下面是开发时用到的命令:

server: buildServer
	@cd config-server; ../bin/settings.exe debug

ahk: buildServer
	@bin/settings.exe GenerateAHK ./data/config.json ./config-server/templates/mykeymap.tmpl ./bin/MyKeymap.ahk

.PHONY: server ahk buildServer buildClientAvalonia copyFiles upload build