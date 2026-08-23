package script

import (
	"fmt"
	"regexp"
	"strings"
)

// 文件类型分组: 分组名 -> 后缀名列表
// AHK 端 (bin/lib/rules/SelectedAction.ahk) 中维护了一份相同的表, 修改时需要同步
var fileGroupExts = map[string][]string{
	"image":   {"jpg", "jpeg", "png", "gif", "bmp", "webp", "svg", "ico"},
	"doc":     {"doc", "docx", "xls", "xlsx", "ppt", "pptx", "pdf", "txt", "md"},
	"code":    {"c", "cpp", "h", "hpp", "java", "py", "js", "ts", "jsx", "tsx", "go", "rs", "rb", "php", "html", "css", "scss", "json", "xml", "yml", "yaml", "sh", "bat"},
	"archive": {"zip", "rar", "7z", "tar", "gz", "bz2", "xz"},
	"video":   {"mp4", "avi", "mkv", "mov", "wmv", "flv", "webm"},
	"audio":   {"mp3", "wav", "flac", "ogg", "aac", "m4a"},
}

// 文本特征 -> 可选行为类型 映射 (单一真源, 需同步的副本):
//   - 前端: config-ui/src/components/action/constants.ts 的 TEXT_TYPE_ACTIONS
//   - AHK 端: bin/lib/rules/SelectedAction.ahk 的 ExecuteActionRule 分支
// 规则: 特征与行为必须语义匹配, 禁止出现「链接 + 程序打开」这类错配组合
var textTypeActions = map[string][]string{
	"url":    {"open_url", "search"},
	"path":   {"open_path", "open_folder"},
	"magnet": {"magnet_download"},
	"plain":  {"open_registry", "search", "run", "send_keys", "script", "copy"},
}

// 特征/行为的中文显示名 (保存校验的错误提示用, 与前端 constants.ts label 保持一致)
var textTypeLabels = map[string]string{
	"url":    "链接",
	"path":   "路径",
	"magnet": "磁力链接",
	"plain":  "纯文本",
}

var actionTypeLabels = map[string]string{
	"open_url":        "默认浏览器打开网址",
	"open_path":       "打开文件/程序 (系统关联)",
	"open_folder":     "打开文件夹",
	"magnet_download": "磁力链接下载",
	"open_registry":   "注册表定位",
}

// ValidateActionSchemeRules 校验方案规则的组合合法性 (textType 特征 -> 行为 必须语义匹配)
// 非法组合返回含中文提示的错误, 调用方应拒绝保存
func ValidateActionSchemeRules(s *ActionScheme) error {
	for i := range s.Rules {
		r := &s.Rules[i]
		if r.MatchType != "textType" {
			continue
		}
		allowed, ok := textTypeActions[r.MatchValue]
		if !ok {
			return fmt.Errorf("未知的文本特征「%s», 可选: 链接 / 路径 / 磁力链接 / 纯文本", r.MatchValue)
		}
		for _, a := range allowed {
			if a == r.ActionType {
				return nil
			}
		}
		return fmt.Errorf("文本特征「%s」与行为「%s」不匹配, 可选行为: %s",
			textTypeLabels[r.MatchValue], actionTypeLabels[r.ActionType], joinActionLabels(allowed))
	}
	return nil
}

// joinActionLabels 把行为类型列表拼接为中文提示 (无显示名的回退原值)
func joinActionLabels(actions []string) string {
	labels := make([]string, 0, len(actions))
	for _, a := range actions {
		if l, ok := actionTypeLabels[a]; ok {
			labels = append(labels, l)
		} else {
			labels = append(labels, a)
		}
	}
	return strings.Join(labels, " / ")
}

// MatchActionScheme 按优先级匹配第一个符合条件的规则, 供模拟测试 API 使用
func MatchActionScheme(scheme *ActionScheme, isFile bool, content string) *ActionRule {
	for i := range scheme.Rules {
		rule := &scheme.Rules[i]
		if matchActionRule(rule, isFile, content) {
			return rule
		}
	}
	return nil
}

func matchActionRule(rule *ActionRule, isFile bool, content string) bool {
	switch rule.MatchType {
	case "fileExt":
		if !isFile {
			return false
		}
		return matchFileExt(rule.MatchValue, content)
	case "fileGroup":
		if !isFile {
			return false
		}
		return matchFileGroup(rule.MatchValue, content)
	case "textType":
		if isFile {
			return false
		}
		return matchTextType(rule.MatchValue, content)
	case "default":
		return true
	}
	return false
}

// matchFileExt 匹配文件后缀, MatchValue 支持逗号分隔多个后缀, "*" 匹配任意文件
func matchFileExt(matchValue, content string) bool {
	if matchValue == "*" {
		return true
	}
	exts := strings.Split(matchValue, ",")
	for _, line := range strings.Split(content, "\n") {
		// fileExt 返回含点后缀, 去掉点后与条件值比较 (与 AHK 端 SplitPath 返回不带点扩展名的语义一致)
		ext := strings.TrimPrefix(fileExt(line), ".")
		if ext == "" {
			continue
		}
		for _, v := range exts {
			v = strings.TrimSpace(v)
			v = strings.TrimPrefix(v, ".")
			if v == "*" || strings.EqualFold(v, ext) {
				return true
			}
		}
	}
	return false
}

// matchFileGroup 匹配文件类型分组, 选中内容中的任意文件命中分组即匹配
func matchFileGroup(group, content string) bool {
	exts, ok := fileGroupExts[strings.ToLower(strings.TrimSpace(group))]
	if !ok {
		return false
	}
	for _, line := range strings.Split(content, "\n") {
		// fileExt 返回含点后缀, 去掉点后与分组表比较 (与 AHK 端 SplitPath 返回不带点扩展名的语义一致)
		ext := strings.TrimPrefix(fileExt(line), ".")
		if ext == "" {
			continue
		}
		for _, v := range exts {
			if strings.EqualFold(v, ext) {
				return true
			}
		}
	}
	return false
}

// matchTextType 匹配文本特征: url(链接) / path(路径) / magnet(磁力链接) / plain(纯文本)
// 与 AHK 端 MatchTextType 保持一致
func matchTextType(t, content string) bool {
	reURL := regexp.MustCompile(`(?i)^(https?|ftp)://`)
	rePath := regexp.MustCompile(`^(\\\\[^\\]+\\[^\\]+|[a-zA-Z]:\\)`)
	reMagnet := regexp.MustCompile(`(?i)^magnet:`)
	switch strings.ToLower(strings.TrimSpace(t)) {
	case "url":
		return reURL.MatchString(content)
	case "path":
		return rePath.MatchString(content)
	case "magnet":
		return reMagnet.MatchString(content)
	case "plain":
		return !reURL.MatchString(content) && !rePath.MatchString(content) && !reMagnet.MatchString(content)
	}
	return false
}

// PreviewAction 生成执行预览: 把 %selected% 替换为选中内容, search 类型返回 URL 编码后的结果
func PreviewAction(rule *ActionRule, content string) string {
	switch rule.ActionType {
	case "search":
		return strings.ReplaceAll(rule.ActionValue, "%selected%", urlEncode(content))
	case "open_url":
		return "用默认浏览器打开: " + content
	case "open_path":
		return "按系统关联程序打开: " + content
	case "open_folder":
		return "打开选中路径所在文件夹"
	case "magnet_download":
		return "用默认 BT 下载工具下载: " + content
	case "open_registry":
		return "打开注册表编辑器并定位: " + content
	default:
		return strings.ReplaceAll(rule.ActionValue, "%selected%", content)
	}
}

// urlEncode 与 AHK 端 URIEncode (Functions.ahk) 行为一致: 非保留字符原样输出, 其余按字节百分号编码
func urlEncode(s string) string {
	var buf strings.Builder
	for _, b := range []byte(s) {
		if (b >= '0' && b <= '9') || (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') {
			buf.WriteByte(b)
		} else {
			buf.WriteString(fmt.Sprintf("%%%02X", b))
		}
	}
	return buf.String()
}

// fileExt 取文件后缀 (含点, 如 ".txt"), 无后缀返回空字符串
func fileExt(path string) string {
	path = strings.TrimSpace(path)
	idx := strings.LastIndexByte(path, '.')
	if idx < 0 || idx == len(path)-1 {
		return ""
	}
	return path[idx:]
}
