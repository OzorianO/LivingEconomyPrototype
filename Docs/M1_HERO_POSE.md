# M1c, контрольна точка 1 — позиція героя і базова first-person камера

18.09.2026. Це частина M1c, не завершення M1. Death/lootable ядро, перенесення
майна з тіла та політика бізнесу після смерті власника ще попереду.

## Реалізовано

- SavedHeroPose: XYZ, поворот моделі, yaw/pitch/distance камери, first-person flag.
  Тип не залежить від Unity. SetHeroPose/GetHeroPose/Capture копіюють дані:
  зовнішній код не може тихо змінити авторитетний snapshot.
- XML v5 за наявності pose. Читач підтримує v1–v5. Неповний v5, NaN/Infinity,
  неприйнятні кути/дистанція відхиляються. Старі дані без pose залишають усе
  майно й отримують стандартну стартову позицію лише при відновленні у сцені.
- Позиція синхронізується перед Save, completed checkpoint, початком дня та
  OnDisable. Це не другий паралельний гаманець/інвентар і не зміна грошей від руху.
- Load, Reset та перебудова світу відновлюють pose. Повторне читання стабільне
  завдяки точності 0.001 для позиції/кутів; швидкість падіння після Load скидається.
- Перевірка у Unity: суша, допустима висота над нею, відсутність статичного
  перешкоджального колайдера в об'ємі CharacterController. Власний герой,
  terrain та NPC capsules виключено з перевірки. Вода, колайдер будівлі або
  висока/підземна позиція дають безпечний fallback на spawn, не знищують майно.
  Числово валідна позиція не гарантує валідності для конкретної геометрії острова.
- V перемикає first/third person у режимі героя. Праве перетягування — огляд,
  WASD/Shift/Space — той самий CharacterController. Eye height 1.6, near clip 0.05.
  Власна primitive модель прихована лише під час активного first-person режиму.
  Overview повертає видиму модель і near clip 0.25; третя особа зберігає camera
  obstacle SphereCast та wheel zoom. Режим overview/exploring не серіалізується.

Це базова камера без рук, нових анімацій, cursor lock, crosshair чи стрільби.
Ці presentation-покращення не блокують M1. Fallback не є respawn після смерті.
Domain reload посеред дня зберігає старе правило: rollback усього дня, включно
з економікою й pose героя, до останнього completed checkpoint.

## Перевірки та межі

Ядро: PASS, 31 156 тверджень (31 134 попередніх + 22 нових для pose),
валідація/копіювання, XML roundtrip, disk, legacy v4 та partial-day rollback.
Компіляція runtime scripts проти Unity assemblies: 0 errors / 0 warnings.
Unity lifecycle: PASS, 200 перевірок (188 попередніх + 12 нових), включно з eye position, body visibility,
movement, Load/precision, overview/third-person, water/building fallback.
Search-index startup errors excluded: 0. [Результат](M1C_POSE_UI_RELOAD_RESULT.txt).
Локальний лог: `.Verification/UnityReload-20260918-a/m1c-pose-verification-retry.log`.

Старі Windows exe залишаються збірками попередніх етапів. Нова standalone
перевірка Save/Load після process restart та death/loot — решта M1c, не DONE.
