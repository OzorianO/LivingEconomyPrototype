# Medieval Colony / Середньовічна колонія — поточний стан

Оновлено: 19.09.2026. Основний проєкт: `D:/UnityProjects/LivingEconomyPrototype`.
HEAD перевірено: `d5e22d4` — великий острів, Axe та ресурсний цикл M2a.

## Готовність

- M0 і M1 завершено: M1a (інвентар), M1b (економіка героя), M1c (стани, камера, Save/Load).
- Є великий terrain-острів, керований герой, 20 NPC, ферма й пекарня та економіка без створення грошей транзакціями.
- Спільний інвентар, купівля/споживання Bread героєм, потреби; збереження позиції та камери, first-person режим.
- Death/loot з атомарним перенесенням особистого майна; бізнес мертвого власника призупиняється. XML v6, читання v1–v6. Це ще не бойова система.
- Після перейменування на Medieval Colony `Load` безпечно читає старий сейв LivingEconomyPrototype, лише якщо нового ще немає. Старий файл не копіюється й не видаляється; наступний `Save` створює новий сейв.
- M2a у роботі: каталог містить Log, Plank, Firewood, Stick і Axe; Save v7 зберігає довільні item ID. ResourceNode/Harvest і три універсальні Recipe/Craft працюють атомарно для героя й NPC через спільний `ExecuteWorldAction`; дерево вимагає Axe, crafting — дерев'яний верстак. Дерево, верстак та сокира представлені у сцені.
- Інвентар героя відкривається кнопкою або клавішею `I`, показує зайняті слоти й усі типи предметів; наявні предмети відображаються першими.
- Підтверджений результат: 31 201 твердження канонічного ядра, 220 Unity lifecycle checks; канонічна Windows-збірка успішна. Окремо перевірено Player: terrain завантажено з `Universal Render Pipeline/Terrain/Lit` і візуально відображається.

## Наступна задача

Продовжити M2a: додати час дії, просте відновлення дерева та Save/Load resource nodes.

## Перевірки та обмеження

З робочої папки `C:/Users/victus16/Documents/ChatGPT/My Game`:

```powershell
dotnet run --project Implementation/Checks/Checks.csproj --configuration Release -p:SimulationSourceDirectory=D:/UnityProjects/LivingEconomyPrototype/Assets/_Project/Scripts/Simulation
dotnet build Implementation/Checks/UnityCompile.csproj --configuration Release
```

Unity: `6000.6.1f1`; lifecycle runner — `Simulation/UnityVerification/ReloadLifecycleVerification.cs`. Для його запуску використати існуючу disposable-копію, не змінювати робочу сцену заради тестів.

Не комітити сторонні зміни матеріалу, Packages/ProjectSettings та `_Recovery`. Запис на D: потребує погодження середовища. Актуальна локальна Windows-збірка M1 перевірена; зміни перейменування, міграції та статусу ще не закомічені й не опубліковані. Перед роботою звірити HEAD/status; після завершення оновити цей файл.
