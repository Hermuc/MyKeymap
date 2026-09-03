package server

import (
	"encoding/json"
	"testing"
)

// TestNilSliceRoundTrip 验证 Hotkeys map 中 value 为 null 时,
// DTOToConfig→ConfigToDTO 往返仍保持 null (非 []), 保证 wire 恒等。
func TestNilSliceRoundTrip(t *testing.T) {
	// 模拟前端 PUT 的 JSON: hotkeys 里某个 key 对应 null (无动作绑定)
	input := `{"keymaps":[{"id":1,"name":"test","enable":true,"hotkey":"","parentID":0,"delay":0,"disableAt":"","hotkeys":{"x":null}}]}`

	var dto ConfigDTO
	if err := json.Unmarshal([]byte(input), &dto); err != nil {
		t.Fatalf("反序列化 DTO 失败: %v", err)
	}

	// DTO → model
	cfg := DTOToConfig(&dto)

	// 验证 model 侧: Hotkeys["x"] 应为 nil
	if cfg.Keymaps[0].Hotkeys["x"] != nil {
		t.Errorf("DTOToConfig: Hotkeys[\"x\"] 应为 nil, 实际为 %v", cfg.Keymaps[0].Hotkeys["x"])
	}

	// model → DTO
	dto2 := ConfigToDTO(cfg)

	// 验证 DTO 侧: Hotkeys["x"] 应为 nil
	if dto2.Keymaps[0].Hotkeys["x"] != nil {
		t.Errorf("ConfigToDTO: Hotkeys[\"x\"] 应为 nil, 实际为 %v", dto2.Keymaps[0].Hotkeys["x"])
	}

	// 最终序列化验证: JSON 中 "x" 的值应为 null 而非 []
	out, err := json.Marshal(dto2)
	if err != nil {
		t.Fatalf("序列化失败: %v", err)
	}
	var check map[string]interface{}
	if err := json.Unmarshal(out, &check); err != nil {
		t.Fatalf("反序列化验证失败: %v", err)
	}
	keymaps := check["keymaps"].([]interface{})
	km := keymaps[0].(map[string]interface{})
	hotkeys := km["hotkeys"].(map[string]interface{})
	if hotkeys["x"] != nil {
		t.Errorf("wire JSON: hotkeys.x 应为 null, 实际为 %v", hotkeys["x"])
	}
}
