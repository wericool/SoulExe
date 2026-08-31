# Наблюдения по макету SoulExe Mobile

## Визуальное направление
- Мобильный portrait UI, премиальный dark/private-salon стиль.
- Фон: глубокий navy/ink около #020617–#051424.
- Акцент: мягкий violet #8B5CF6 / #D0BCFF, без неоновой перегрузки.
- Поверхности: слоистые тёмно-синие карточки, glassmorphism для верхней/нижней навигации.
- Шрифты: Manrope для заголовков и UI, Inter для основного текста.
- Скругления: карточки 16px, pill-переключатели и кнопки, круглые аватары.

## Видимые состояния
- Splash: центральный светящийся ромб/звезда, название SoulExe, статус «Initiating neural sequence…», тонкий violet progress bar снизу.
- Library: верхняя панель с back, заголовком «Библиотека», overflow; переключатель «Персонажи / Персоны»; список карточек персонажей с аватаром, именем и описанием; floating action button «+»; нижняя навигация «Разговоры / Библиотека / Ещё».
- Персонажи: Элара, Маркус, Кибер-советник. Карточки используют тёмную tonal layering и мягкие границы.

## Предполагаемая игровая адаптация
- Сделать интерактивную narrative/chat игру: игрок выбирает персонажа, читает сцену и отвечает через composer или быстрые варианты.
- Состояния: splash -> library -> conversation/game scene -> choice/result overlay.
- Сохранить messenger logic: user bubble справа, AI/character bubble слева, Director text-only centered.

## Дополнительные наблюдения
- Экран разговора использует высокий верхний app bar: back, круглый avatar, имя персонажа, статус «печатает», overflow.
- Сцена: centered location/status chip, крупные AI bubbles слева и user bubble справа, Director-сообщение без контейнера по центру, затем ещё одна реплика.
- Нижний composer состоит из glass control row с круглой кнопкой skip/play, pill «Персона» с chevron, кнопкой media; ниже — большой pill input «Напишите сообщение…» и круглая violet send button.
- Экран списка разговоров использует верхний заголовок SoulExe, search и overflow, карточки с avatar/title/preview/time/status, FAB «+» и tab bar с активной вкладкой «Разговоры».

## Логотип
Сгенерирован квадратный full-bleed знак: четырёхконечный crystal-star с фиолетовым ядром, бело-лавандовым свечением и midnight navy фоном. Он читается как отдельная иконка без текста и используется в icon.png, splash-icon.png, favicon.png и android-icon-foreground.png.
