'use strict';

document.addEventListener('DOMContentLoaded', () => {
    const searchBtn = document.getElementById('searchBtn');
    const exportBtn = document.getElementById('exportBtn');
    const resultsSection = document.getElementById('resultsSection');
    const resultsBody = document.getElementById('resultsBody');
    const resultCount = document.getElementById('resultCount');

    // Элементы пагинации
    const paginationControls = document.getElementById('paginationControls');
    const pageInfo = document.getElementById('pageInfo');
    const prevPageBtn = document.getElementById('prevPageBtn');
    const nextPageBtn = document.getElementById('nextPageBtn');
    const exportFormat = document.getElementById('exportFormat');

    if (!searchBtn) return;

    let currentPage = 1;
    const pageSize = 50; // Количество записей на одной странице

    // Сборка фильтров
    function getFilters() {
        // Собираем все отмеченные чекбоксы типов
        const selectedTypes = Array.from(document.querySelectorAll('.type-cb:checked')).map(cb => cb.value);

        // Собираем все отмеченные чекбоксы вендоров
        const selectedVendors = Array.from(document.querySelectorAll('.vendor-cb:checked')).map(cb => cb.value);

        return {
            types: selectedTypes,
            vendors: selectedVendors,
            value: document.getElementById('fValue').value,
            comment: document.getElementById('fComment').value,
            dateFrom: document.getElementById('fDateFrom').value || null,
            dateTo: document.getElementById('fDateTo').value || null,
            isMultipleReported: document.getElementById('fMulti').checked ? true : null,
            page: currentPage,
            pageSize: pageSize
        };
    }

    // Выполнение поиска
    async function executeSearch() {
        searchBtn.disabled = true;
        searchBtn.textContent = 'Поиск...';

        try {
            const res = await fetch('/api/iocmanage/search', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(getFilters())
            });

            const data = await res.json();
            if (!res.ok) throw new Error(data.error || 'Ошибка');

            renderResults(data);

            // Активируем кнопку скачивания только если что-то нашли
            exportBtn.disabled = data.totalCount === 0;

            // Управление блоком пагинации
            if (data.totalPages > 1) {
                paginationControls.style.display = 'flex';
                pageInfo.textContent = `Страница ${data.page} из ${data.totalPages} (Всего: ${data.totalCount})`;
                prevPageBtn.disabled = data.page <= 1;
                nextPageBtn.disabled = data.page >= data.totalPages;
            } else {
                paginationControls.style.display = 'none';
            }
        } catch (err) {
            alert('Ошибка: ' + err.message);
        } finally {
            searchBtn.disabled = false;
            searchBtn.textContent = 'Поиск';
        }
    }

    // Обработчик кнопки Поиск (сбрасываем на первую страницу)
    searchBtn.addEventListener('click', () => {
        currentPage = 1;
        executeSearch();
    });

    // Обработчики пагинации
    if (prevPageBtn) {
        prevPageBtn.addEventListener('click', () => {
            if (currentPage > 1) {
                currentPage--;
                executeSearch();
            }
        });
    }

    if (nextPageBtn) {
        nextPageBtn.addEventListener('click', () => {
            currentPage++;
            executeSearch();
        });
    }

    // Выгрузка файла (CSV или TXT)
    if (exportBtn) {
        exportBtn.addEventListener('click', async () => {
            exportBtn.disabled = true;
            exportBtn.textContent = 'Скачивание...';

            const filters = getFilters();
            filters.exportFormat = exportFormat ? exportFormat.value : 'csv';

            try {
                const res = await fetch('/api/iocmanage/export', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(filters)
                });

                if (!res.ok) throw new Error('Ошибка при генерации файла');

                const blob = await res.blob();
                const url = window.URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;

                // Определяем расширение для файла
                const ext = filters.exportFormat === 'txt' ? 'txt' : 'csv';
                a.download = `ioc_export_${new Date().getTime()}.${ext}`;

                a.click();
                window.URL.revokeObjectURL(url);
            } catch (err) {
                alert('Ошибка: ' + err.message);
            } finally {
                exportBtn.disabled = false;
                exportBtn.textContent = 'Скачать';
            }
        });
    }

    // Отрисовка таблицы
    function renderResults(data) {
        resultsSection.style.display = 'block';
        resultCount.textContent = data.totalCount;
        resultsBody.innerHTML = '';

        if (!data.items || data.items.length === 0) {
            resultsBody.innerHTML = '<tr><td colspan="7" class="text-center text-muted py-4">Ничего не найдено</td></tr>';
            return;
        }

        data.items.forEach(ioc => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td><span class="badge bg-secondary">${escapeHtml(ioc.type)}</span></td>
                <td class="text-break">${escapeHtml(ioc.value)}</td>
                <td>${escapeHtml(ioc.iocVendor || '—')}</td>
                <td>${new Date(ioc.firstSeen).toLocaleDateString()}</td>
                <td>${new Date(ioc.lastSeen).toLocaleDateString()}</td>
                <td>${ioc.reportCount}</td>
                <td>${ioc.isMultipleReported ? '✅' : '—'}</td>
            `;
            resultsBody.appendChild(tr);
        });
    }

    function escapeHtml(s) {
        if (!s) return '';
        return String(s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }
});