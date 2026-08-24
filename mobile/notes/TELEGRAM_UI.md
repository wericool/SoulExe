# Telegram-like UI in SoulExe Mobile

## Java / native Telegram
Исходники Telegram Android (`DrKLO/Telegram`, `ChatActivityEnterView.java`) на Java **нельзя** просто вставить в Expo/React Native проект.
Паттерны перенесены в JS/TS:

- `components/soul/ChatActivityEnterView.tsx` — растущее поле, круглая send, idle/active
- `components/soul/messenger-elements.tsx` — строки диалогов ~72dp, шапка треда
- `lib/theme.ts` — палитра Night (не «чёрные блоки»)

## Поведение как в TG (в рамках SoulExe)
1. Список чатов/сцен — плоские строки, время справа, превью снизу
2. Тред — пузыри in/out без толстых рамок, лента снизу
3. Enter view — как ChatActivityEnterView: min/max высота, send только при тексте
4. Сцены — отдельная панель действий (старт/пауза/ход) рядом с полем режиссёра
