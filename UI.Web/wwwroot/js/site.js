document.addEventListener('DOMContentLoaded', () => {

    // ==========================================
    // 1. УПРАВЛЕНИЕ ТЕМАМИ
    // ==========================================
    const themeSelect = document.getElementById('themeSelect');
    if (themeSelect) {
        function applyTheme(themeName) {
            // Удаляем все старые классы тем
            document.body.className = document.body.className.replace(/theme-\w+/g, '').trim();
            // Добавляем новый
            document.body.classList.add(themeName);
            // Сохраняем в localStorage
            localStorage.setItem('theme', themeName);
            // Синхронизируем селект
            themeSelect.value = themeName;
        }

        // Загрузка при старте
        const savedTheme = localStorage.getItem('theme') || 'theme-default';
        applyTheme(savedTheme);

        // Слушатель изменения
        themeSelect.addEventListener('change', (e) => {
            applyTheme(e.target.value);
        });
    }

    // ==========================================
    // 2. ИНДИКАТОР WHOIS
    // ==========================================
    const whoisCircle = document.getElementById('whoisCircle');
    const whoisDropdown = document.getElementById('whoisDropdown');
    const dotRadb = document.getElementById('dotRadb');
    const dotArin = document.getElementById('dotArin');
    const whoisRefresh = document.getElementById('whoisRefresh');

    if (whoisCircle) {
        // Открытие/закрытие выпадающего меню
        whoisCircle.addEventListener('click', (e) => {
            e.stopPropagation();
            whoisDropdown.classList.toggle('open');
        });

        // Закрытие меню при клике вне его
        document.addEventListener('click', (e) => {
            if (whoisDropdown.classList.contains('open') && !whoisDropdown.contains(e.target)) {
                whoisDropdown.classList.remove('open');
            }
        });

        // Функция проверки статуса
        async function checkWhoisStatus(forceRefresh = false) {
            whoisCircle.className = 'whois-circle gray';
            whoisCircle.title = 'Проверка whois...';

            try {
                const url = `/api/whois/status${forceRefresh ? '?refresh=true' : ''}`;
                const response = await fetch(url);
                if (!response.ok) throw new Error('Ошибка сети');

                const data = await response.json();

                // Обновляем точки в меню
                dotRadb.className = `whois-dot ${data.radb ? 'green' : 'red'}`;
                dotArin.className = `whois-dot ${data.arin ? 'green' : 'red'}`;

                // Обновляем главный индикатор
                if (data.radb && data.arin) {
                    whoisCircle.className = 'whois-circle green';
                    whoisCircle.title = 'Все WHOIS серверы доступны';
                } else if (data.radb || data.arin) {
                    whoisCircle.className = 'whois-circle yellow';
                    whoisCircle.title = 'Доступен только один WHOIS сервер';
                } else {
                    whoisCircle.className = 'whois-circle red';
                    whoisCircle.title = 'WHOIS серверы недоступны';
                }

            } catch (error) {
                console.error('Whois check failed:', error);
                whoisCircle.className = 'whois-circle red';
                whoisCircle.title = 'Ошибка проверки';
                dotRadb.className = 'whois-dot red';
                dotArin.className = 'whois-dot red';
            }
        }

        // Кнопка "Обновить"
        if (whoisRefresh) {
            whoisRefresh.addEventListener('click', () => {
                checkWhoisStatus(true);
            });
        }

        // Первичная проверка при загрузке страницы (с небольшой задержкой, чтобы не тормозить рендер)
        setTimeout(() => checkWhoisStatus(false), 500);

        // Автообновление каждые 30 секунд
        setInterval(() => checkWhoisStatus(false), 30000);
    }
});