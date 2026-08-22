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

// ValidateActionSchemeRegexes 预检所有 textRegex 规则的正则能否在 Go RE2 下编译
// Go 的 regexp 是 RE2 (不支持前瞻/回溯等 PCRE 语法), 而 AHK 端 RegExMatch 用 PCRE2,
// 编译失败时返回该正则与错误, 调用方应明确提示用户而非误报"不匹配"
func ValidateActionSchemeRegexes(s *ActionScheme) (badValue string, err error) {
	for i := range s.Rules {
		r := &s.Rules[i]
		if r.MatchType == "textRegex" && r.MatchValue != "" {
			if _, err := regexp.Compile(r.MatchValue); err != nil {
				return r.MatchValue, err
			}
		}
	}
	return "", nil
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
	case "textRegex":
		if isFile {
			return false
		}
		re, err := regexp.Compile(rule.MatchValue)
		if err != nil {
			return false
		}
		return re.MatchString(content)
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

// matchTextType 匹配文本特征: url(链接) / path(路径) / plain(纯文本)
func matchTextType(t, content string) bool {
	reURL := regexp.MustCompile(`(?i)^(https?|ftp)://`)
	rePath := regexp.MustCompile(`^(\\\\[^\\]+\\[^\\]+|[a-zA-Z]:\\)`)
	switch strings.ToLower(strings.TrimSpace(t)) {
	case "url":
		return reURL.MatchString(content)
	case "path":
		return rePath.MatchString(content)
	case "plain":
		return !reURL.MatchString(content) && !rePath.MatchString(content)
	}
	return false
}

// PreviewAction 生成执行预览: 把 %selected% 替换为选中内容, search 类型返回 URL 编码后的结果
func PreviewAction(rule *ActionRule, content string) string {
	switch rule.ActionType {
	case "search":
		return strings.ReplaceAll(rule.ActionValue, "%selected%", urlEncode(content))
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
