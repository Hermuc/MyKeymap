package generators

import (
	"testing"

	"settings/internal/script/model"
)

func TestSortActions(t *testing.T) {
	a := model.Action{WindowGroupID: 0, Comment: "item1"}
	b := model.Action{WindowGroupID: 1, Comment: "item2"}
	c := model.Action{WindowGroupID: 0, Comment: "item3"}
	s := []model.Action{a, b, c}
	ss := sortActions(s)
	assertEqual(t, ss[0], b)
	assertEqual(t, ss[1], a)
	assertEqual(t, ss[2], c)
}

func assertEqual[T comparable](t *testing.T, a, b T) {
	t.Helper()
	if a != b {
		t.Errorf("%v != %v", a, b)
	}
}
