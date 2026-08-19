// Composables
import { createRouter, createWebHistory } from 'vue-router'
import CustomHotkey from "@/views/CustomHotkey.vue";
import Abbr from "@/views/Abbr.vue";
import HomeSettings from '@/views/HomeSettings.vue';
import Keymap from "@/views/Keymap.vue";
import SelectedAction from "@/views/SelectedAction.vue";
import SelectedActionEdit from "@/views/SelectedActionEdit.vue";

const routes = [
  {
    path: "/",
    component: HomeSettings
  },
  {
    path: "/settings",
    component: HomeSettings
  },
  {
    path: "/keymap",
    children: [
      {
        path: ':id(1)',
        component: CustomHotkey
      },
      {
        path: ':id(2|3)',
        component: Abbr
      },
      {
        path: ':id(\\d+)',
        component: Keymap
      },
      {
        path: 'action',
        component: SelectedAction
      },
      {
        path: 'action/:id(\\d+)',
        component: SelectedActionEdit
      },
    ]
  },
]

const router = createRouter({
  history: createWebHistory(process.env.BASE_URL),
  routes,
})

export default router
