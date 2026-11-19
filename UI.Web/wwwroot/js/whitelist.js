'use strict';

document.addEventListener('DOMContentLoaded', () => {
    const addBtn = document.getElementById('addBtn');
    const wType = document.getElementById('wType');
    const wValue = document.getElementById('wValue');
    const wComment = document.getElementById('wComment');
    const wSearch = document.getElementById('wSearch');
    const errorBox = document.getElementById('errorBox');
    const listBody = document.getElementById('whitelistBody');

    if (!addBtn) return;

    let allItems = []; // Храним все элементы для клиентского поиска

    async function loadList() {
        try {
            const res = await fetch('/api/whitelist');
            allItems = await res.json();
            applyFilter();
        } catch {
            listBody.innerHTML = '<tr><td colspan="5" class="text-danger">Ошибка загрузки</td></tr>';
        }
    }

    function applyFilter() {
        const query = wSearch.value.toLowerCase().trim();

        // Получаем список выбранных типов
        const selectedTypes = Array.from(document.querySelectorAll('.w-type-cb:checked')).map(cb => cb.value.toLowerCase());

        const filtered = allItems.filter(item => {
            // Проверка по типу (если ничего не выбрано, показываем всё)
            const matchType = selectedTypes.length === 0 || selectedTypes.includes(item.type.toLowerCase());

            // Проверка по тексту
            const val = item.value.toLowerCase();
            const com = (item.comment || '').toLowerCase();
            const matchText = val.includes(query) || com.includes(query);

            return matchType && matchText;
        });

        renderTable(filtered);
    }

    // Добавляем слушатели событий на чекбоксы, чтобы таблица перерисовывалась мгновенно
    document.querySelectorAll('.w-type-cb').forEach(cb => {
        cb.addEventListener('change', applyFilter);
    });

    function renderTable(items) {
        listBody.innerHTML = '';
        if (items.length === 0) {
            listBody.innerHTML = '<tr><td colspan="5" class="text-center text-muted">Ничего не найдено</td></tr>';
            return;
        }

        items.forEach(item => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td><span class="badge bg-secondary">${escapeHtml(item.type)}</span></td>
                <td class="text-break">${escapeHtml(item.value)}</td>
                <td class="text-muted small">${escapeHtml(item.comment || '—')}</td>
                <td>${new Date(item.addedAt).toLocaleString()}</td>
                <td class="text-end">
                    <button class="btn btn-sm btn-outline-danger" onclick="deleteItem(${item.id})">Удалить</button>
                </td>
            `;
            listBody.appendChild(tr);
        });
    }

    // Обработчик поиска (мгновенный)
    wSearch.addEventListener('input', applyFilter);

    addBtn.addEventListener('click', async () => {
        errorBox.style.display = 'none';
        const type = wType.value;
        const value = wValue.value.trim();
        const comment = wComment.value.trim();

        if (!value) {
            errorBox.textContent = 'Значение не может быть пустым.';
            errorBox.style.display = 'block';
            return;
        }

        // НОВАЯ ПРОВЕРКА КОММЕНТАРИЯ
        if (!comment) {
            errorBox.textContent = 'Пожалуйста, укажите причину добавления в белый список.';
            errorBox.style.display = 'block';

            // Подсвечиваем поле красным
            wComment.classList.add('is-invalid');
            wComment.focus();
            setTimeout(() => wComment.classList.remove('is-invalid'), 2000);
            return;
        }

        addBtn.disabled = true;
        try {
            const res = await fetch('/api/whitelist', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ type: type, value: value, comment: comment })
            });

            const data = await res.json();
            if (!res.ok) throw new Error(data.error || 'Ошибка');

            wValue.value = '';
            wComment.value = '';
            loadList();
        } catch (err) {
            errorBox.textContent = err.message;
            errorBox.style.display = 'block';
        } finally {
            addBtn.disabled = false;
        }
    });

    window.deleteItem = async (id) => {
        if (!confirm('Удалить из белого списка?')) return;
        try {
            await fetch(`/api/whitelist/${id}`, { method: 'DELETE' });
            loadList();
        } catch {
            alert('Ошибка при удалении');
        }
    };

    function escapeHtml(s) {
        if (!s) return '';
        return String(s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    // Скрываем ошибку при вводе
    wComment.addEventListener('input', () => errorBox.style.display = 'none');
    wValue.addEventListener('input', () => errorBox.style.display = 'none');

    // ==========================================
    // МАССОВАЯ ЗАГРУЗКА TXT
    // ==========================================
    const wDropzone = document.getElementById('wDropzone');
    const wFileInput = document.getElementById('wFileInput');
    const wUploadStatus = document.getElementById('wUploadStatus');

    if (wDropzone && wFileInput) {
        // ЗАЩИТА ОТ БЕСКОНЕЧНОГО ЦИКЛА:
        wDropzone.addEventListener('click', (e) => {
            if (e.target === wFileInput) return;
            wFileInput.click();
        });

        wFileInput.addEventListener('change', () => {
            if (wFileInput.files.length > 0) {
                processTxtFile(wFileInput.files[0]);
                wFileInput.value = '';
            }
        });

        ['dragenter', 'dragover'].forEach(evt =>
            wDropzone.addEventListener(evt, e => { e.preventDefault(); e.stopPropagation(); wDropzone.classList.add('dragover'); })
        );
        ['dragleave', 'drop'].forEach(evt =>
            wDropzone.addEventListener(evt, e => { e.preventDefault(); e.stopPropagation(); wDropzone.classList.remove('dragover'); })
        );
        wDropzone.addEventListener('drop', e => {
            if (e.dataTransfer.files.length > 0) {
                processTxtFile(e.dataTransfer.files[0]);
            }
        });
    }

    async function processTxtFile(file) {
        if (!file.name.toLowerCase().endsWith('.txt')) {
            alert('Пожалуйста, выберите файл с расширением .txt');
            return;
        }

        const comment = wComment.value.trim();
        if (!comment) {
            alert('Пожалуйста, заполните поле "Комментарий" перед загрузкой файла.');
            wComment.focus();
            wComment.classList.add('is-invalid');
            setTimeout(() => wComment.classList.remove('is-invalid'), 2000);
            return;
        }

        wUploadStatus.textContent = 'Чтение файла...';
        wUploadStatus.style.display = 'block';

        const reader = new FileReader();
        reader.onload = async (e) => {
            const text = e.target.result;
            // Разбиваем по строкам, убираем пустые строки
            const lines = text.split(/\r?\n/).map(l => l.trim()).filter(l => l.length > 0);

            if (lines.length === 0) {
                alert('Файл пуст.');
                wUploadStatus.style.display = 'none';
                return;
            }

            wUploadStatus.textContent = `Отправка ${lines.length} записей...`;

            try {
                const res = await fetch('/api/whitelist/bulk', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        values: lines,
                        type: wType.value,
                        comment: comment
                    })
                });

                const data = await res.json();
                if (!res.ok) throw new Error(data.error || 'Ошибка сервера');

                alert(`Успешно добавлено: ${data.added}\nПропущено (уже были в списке): ${data.skipped}`);
                loadList(); // Обновляем таблицу
            } catch (err) {
                alert('Ошибка: ' + err.message);
            } finally {
                wUploadStatus.style.display = 'none';
            }
        };

        reader.readAsText(file);
    }

    loadList();
});