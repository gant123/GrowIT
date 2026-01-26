// ========================================
// GROWIT ENTERPRISE APPLICATION
// Theme Management, Interactions & Utilities
// ========================================

(function() {
    'use strict';

    // ========================================
    // THEME MANAGER
    // Enterprise Light/Dark Theme System
    // ========================================

    window.themeManager = {
        THEME_KEY: 'growit-theme',
        
        getTheme: function() {
            const saved = localStorage.getItem(this.THEME_KEY);
            if (saved) return saved;
            
            if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
                return 'dark';
            }
            return 'light';
        },
        
        setTheme: function(theme) {
            localStorage.setItem(this.THEME_KEY, theme);
            document.documentElement.setAttribute('data-theme', theme);
            
            const metaTheme = document.querySelector('meta[name="theme-color"]');
            if (metaTheme) {
                metaTheme.setAttribute('content', theme === 'dark' ? '#0f172a' : '#f8fafc');
            }
            
            window.dispatchEvent(new CustomEvent('themeChanged', { detail: { theme } }));
        },
        
        toggle: function() {
            const current = this.getTheme();
            const newTheme = current === 'dark' ? 'light' : 'dark';
            this.setTheme(newTheme);
            return newTheme;
        },
        
        init: function() {
            const theme = this.getTheme();
            document.documentElement.setAttribute('data-theme', theme);
            
            if (window.matchMedia) {
                window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => {
                    if (!localStorage.getItem(this.THEME_KEY)) {
                        this.setTheme(e.matches ? 'dark' : 'light');
                    }
                });
            }
        }
    };

    // ========================================
    // SIDEBAR MANAGER
    // Collapsible Navigation
    // ========================================

    window.sidebarManager = {
        EXPANDED_KEY: 'growit-sidebar-expanded',
        
        isExpanded: function() {
            const saved = localStorage.getItem(this.EXPANDED_KEY);
            return saved !== 'false';
        },
        
        toggle: function() {
            const sidebar = document.querySelector('.growit-sidebar');
            const main = document.querySelector('.growit-main');
            
            if (sidebar) {
                const isExpanded = !sidebar.classList.contains('collapsed');
                sidebar.classList.toggle('collapsed', isExpanded);
                main?.classList.toggle('sidebar-collapsed', isExpanded);
                localStorage.setItem(this.EXPANDED_KEY, !isExpanded);
            }
        },
        
        openMobile: function() {
            const sidebar = document.querySelector('.growit-sidebar');
            const overlay = document.querySelector('.sidebar-overlay');
            
            sidebar?.classList.add('mobile-expanded');
            overlay?.classList.add('active');
            document.body.style.overflow = 'hidden';
        },
        
        closeMobile: function() {
            const sidebar = document.querySelector('.growit-sidebar');
            const overlay = document.querySelector('.sidebar-overlay');
            
            sidebar?.classList.remove('mobile-expanded');
            overlay?.classList.remove('active');
            document.body.style.overflow = '';
        },
        
        init: function() {
            const isExpanded = this.isExpanded();
            const sidebar = document.querySelector('.growit-sidebar');
            const main = document.querySelector('.growit-main');
            
            if (!isExpanded && sidebar) {
                sidebar.classList.add('collapsed');
                main?.classList.add('sidebar-collapsed');
            }
        }
    };

    // ========================================
    // TOAST NOTIFICATIONS
    // ========================================

    window.toastManager = {
        container: null,
        defaultDuration: 4000,
        
        init: function() {
            if (!this.container) {
                this.container = document.createElement('div');
                this.container.className = 'toast-container';
                this.container.setAttribute('aria-live', 'polite');
                this.container.setAttribute('aria-atomic', 'true');
                document.body.appendChild(this.container);
            }
        },
        
        show: function(message, type = 'info', duration = null) {
            this.init();
            
            const toast = document.createElement('div');
            toast.className = `toast toast-${type}`;
            
            const icons = {
                success: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>',
                danger: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>',
                warning: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
                info: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>'
            };
            
            toast.innerHTML = `
                <span class="toast-icon">${icons[type] || icons.info}</span>
                <span class="toast-message">${this.escapeHtml(message)}</span>
                <button class="toast-close" aria-label="Close notification">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                    </svg>
                </button>
            `;
            
            const closeBtn = toast.querySelector('.toast-close');
            closeBtn.addEventListener('click', () => this.dismiss(toast));
            
            this.container.appendChild(toast);
            
            // Trigger animation
            requestAnimationFrame(() => {
                toast.classList.add('show');
            });
            
            // Auto dismiss
            const dismissTime = duration || this.defaultDuration;
            if (dismissTime > 0) {
                setTimeout(() => this.dismiss(toast), dismissTime);
            }
            
            return toast;
        },
        
        dismiss: function(toast) {
            if (!toast || toast.classList.contains('hiding')) return;
            
            toast.classList.add('hiding');
            toast.classList.remove('show');
            
            setTimeout(() => {
                toast.remove();
            }, 300);
        },
        
        success: function(message, duration) {
            return this.show(message, 'success', duration);
        },
        
        danger: function(message, duration) {
            return this.show(message, 'danger', duration);
        },
        
        warning: function(message, duration) {
            return this.show(message, 'warning', duration);
        },
        
        info: function(message, duration) {
            return this.show(message, 'info', duration);
        },
        
        escapeHtml: function(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }
    };

    // ========================================
    // MODAL MANAGER
    // ========================================

    window.modalManager = {
        activeModals: [],
        
        open: function(modalId) {
            const modal = document.getElementById(modalId);
            if (!modal) return;
            
            modal.classList.add('active');
            document.body.classList.add('modal-open');
            this.activeModals.push(modalId);
            
            // Focus first focusable element
            const focusable = modal.querySelector('input, button, select, textarea, [tabindex]:not([tabindex="-1"])');
            focusable?.focus();
            
            // Trap focus within modal
            this.trapFocus(modal);
        },
        
        close: function(modalId) {
            const modal = modalId ? document.getElementById(modalId) : document.querySelector('.modal-overlay.active');
            if (!modal) return;
            
            modal.classList.remove('active');
            this.activeModals = this.activeModals.filter(id => id !== (modalId || modal.id));
            
            if (this.activeModals.length === 0) {
                document.body.classList.remove('modal-open');
            }
        },
        
        closeAll: function() {
            document.querySelectorAll('.modal-overlay.active').forEach(modal => {
                modal.classList.remove('active');
            });
            this.activeModals = [];
            document.body.classList.remove('modal-open');
        },
        
        trapFocus: function(modal) {
            const focusableElements = modal.querySelectorAll(
                'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
            );
            const firstFocusable = focusableElements[0];
            const lastFocusable = focusableElements[focusableElements.length - 1];
            
            modal.addEventListener('keydown', function(e) {
                if (e.key !== 'Tab') return;
                
                if (e.shiftKey) {
                    if (document.activeElement === firstFocusable) {
                        lastFocusable.focus();
                        e.preventDefault();
                    }
                } else {
                    if (document.activeElement === lastFocusable) {
                        firstFocusable.focus();
                        e.preventDefault();
                    }
                }
            });
        }
    };

    // ========================================
    // DROPDOWN MANAGER
    // ========================================

    window.dropdownManager = {
        activeDropdown: null,
        
        toggle: function(triggerId) {
            const trigger = document.getElementById(triggerId);
            const dropdown = trigger?.nextElementSibling;
            
            if (!dropdown) return;
            
            const isOpen = dropdown.classList.contains('open');
            
            // Close any open dropdown
            this.closeAll();
            
            if (!isOpen) {
                dropdown.classList.add('open');
                trigger.setAttribute('aria-expanded', 'true');
                this.activeDropdown = dropdown;
                this.positionDropdown(trigger, dropdown);
            }
        },
        
        closeAll: function() {
            document.querySelectorAll('.dropdown-menu.open').forEach(dropdown => {
                dropdown.classList.remove('open');
                dropdown.previousElementSibling?.setAttribute('aria-expanded', 'false');
            });
            this.activeDropdown = null;
        },
        
        positionDropdown: function(trigger, dropdown) {
            const rect = trigger.getBoundingClientRect();
            const spaceBelow = window.innerHeight - rect.bottom;
            const spaceAbove = rect.top;
            
            if (spaceBelow < 200 && spaceAbove > spaceBelow) {
                dropdown.classList.add('dropdown-up');
            } else {
                dropdown.classList.remove('dropdown-up');
            }
        },
        
        init: function() {
            document.addEventListener('click', (e) => {
                if (!e.target.closest('.dropdown')) {
                    this.closeAll();
                }
            });
        }
    };

    // ========================================
    // UTILITY FUNCTIONS
    // ========================================

    window.growITUtils = {
        formatCurrency: function(amount, currency = 'USD') {
            return new Intl.NumberFormat('en-US', {
                style: 'currency',
                currency: currency,
                minimumFractionDigits: 0,
                maximumFractionDigits: 0
            }).format(amount);
        },
        
        formatCurrencyPrecise: function(amount, currency = 'USD') {
            return new Intl.NumberFormat('en-US', {
                style: 'currency',
                currency: currency,
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            }).format(amount);
        },
        
        formatNumber: function(num) {
            if (num >= 1000000) {
                return (num / 1000000).toFixed(1) + 'M';
            }
            if (num >= 1000) {
                return (num / 1000).toFixed(1) + 'K';
            }
            return num.toString();
        },
        
        formatDate: function(date, format = 'short') {
            const d = new Date(date);
            const options = format === 'short' 
                ? { month: 'short', day: 'numeric', year: 'numeric' }
                : { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' };
            return d.toLocaleDateString('en-US', options);
        },
        
        formatRelativeTime: function(date) {
            const now = new Date();
            const d = new Date(date);
            const diff = now - d;
            
            const seconds = Math.floor(diff / 1000);
            const minutes = Math.floor(seconds / 60);
            const hours = Math.floor(minutes / 60);
            const days = Math.floor(hours / 24);
            const weeks = Math.floor(days / 7);
            const months = Math.floor(days / 30);
            
            if (seconds < 60) return 'Just now';
            if (minutes < 60) return `${minutes}m ago`;
            if (hours < 24) return `${hours}h ago`;
            if (days < 7) return `${days}d ago`;
            if (weeks < 4) return `${weeks}w ago`;
            if (months < 12) return `${months}mo ago`;
            return this.formatDate(date);
        },
        
        getInitials: function(name) {
            if (!name) return '?';
            const parts = name.trim().split(/[\s@.]+/);
            if (parts.length >= 2) {
                return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
            }
            return name.substring(0, 2).toUpperCase();
        },
        
        copyToClipboard: async function(text) {
            try {
                await navigator.clipboard.writeText(text);
                toastManager.success('Copied to clipboard');
                return true;
            } catch {
                const textarea = document.createElement('textarea');
                textarea.value = text;
                textarea.style.position = 'fixed';
                textarea.style.opacity = '0';
                document.body.appendChild(textarea);
                textarea.select();
                document.execCommand('copy');
                document.body.removeChild(textarea);
                toastManager.success('Copied to clipboard');
                return true;
            }
        },
        
        debounce: function(func, wait) {
            let timeout;
            return function executedFunction(...args) {
                const later = () => {
                    clearTimeout(timeout);
                    func(...args);
                };
                clearTimeout(timeout);
                timeout = setTimeout(later, wait);
            };
        },
        
        throttle: function(func, limit) {
            let inThrottle;
            return function(...args) {
                if (!inThrottle) {
                    func.apply(this, args);
                    inThrottle = true;
                    setTimeout(() => inThrottle = false, limit);
                }
            };
        },
        
        scrollToElement: function(selector, offset = 80) {
            const element = document.querySelector(selector);
            if (element) {
                const top = element.getBoundingClientRect().top + window.pageYOffset - offset;
                window.scrollTo({ top, behavior: 'smooth' });
            }
        },
        
        generateId: function() {
            return 'id-' + Math.random().toString(36).substr(2, 9);
        },
        
        // Season/Phase helpers
        getPhaseColor: function(phase) {
            const colors = {
                'crisis': 'var(--growit-danger)',
                'planting': 'var(--growit-warning)',
                'growing': 'var(--growit-info)',
                'harvest': 'var(--growit-success)'
            };
            return colors[phase?.toLowerCase()] || 'var(--color-text-secondary)';
        },
        
        getPhaseLabel: function(phase) {
            const labels = {
                'crisis': 'Crisis Season',
                'planting': 'Planting Season',
                'growing': 'Growing Season',
                'harvest': 'Harvest Season'
            };
            return labels[phase?.toLowerCase()] || phase;
        },
        
        // Stability score color
        getStabilityColor: function(score) {
            if (score >= 80) return 'var(--growit-success)';
            if (score >= 60) return 'var(--growit-info)';
            if (score >= 40) return 'var(--growit-warning)';
            return 'var(--growit-danger)';
        }
    };

    // ========================================
    // KEYBOARD SHORTCUTS
    // ========================================

    window.keyboardShortcuts = {
        shortcuts: {},
        
        register: function(key, callback, description) {
            this.shortcuts[key] = { callback, description };
        },
        
        init: function() {
            // Cmd/Ctrl + K: Focus search
            this.register('ctrl+k', () => {
                const searchInput = document.querySelector('.topbar-search input, .search-box input');
                searchInput?.focus();
            }, 'Focus search');
            
            // Cmd/Ctrl + N: New (Plant Seed)
            this.register('ctrl+n', () => {
                const plantSeedBtn = document.querySelector('.btn-plant-seed');
                plantSeedBtn?.click();
            }, 'New Growth Plan');
            
            // Cmd/Ctrl + /: Show shortcuts
            this.register('ctrl+/', () => {
                this.showHelp();
            }, 'Show shortcuts');
            
            document.addEventListener('keydown', (e) => {
                // Build key combo string
                const parts = [];
                if (e.ctrlKey || e.metaKey) parts.push('ctrl');
                if (e.shiftKey) parts.push('shift');
                if (e.altKey) parts.push('alt');
                parts.push(e.key.toLowerCase());
                
                const combo = parts.join('+');
                const shortcut = this.shortcuts[combo];
                
                if (shortcut && !this.isInputFocused()) {
                    e.preventDefault();
                    shortcut.callback();
                }
                
                // Escape: Close modals/dropdowns
                if (e.key === 'Escape') {
                    modalManager.close();
                    dropdownManager.closeAll();
                    sidebarManager.closeMobile();
                }
            });
        },
        
        isInputFocused: function() {
            const active = document.activeElement;
            return active && (
                active.tagName === 'INPUT' ||
                active.tagName === 'TEXTAREA' ||
                active.isContentEditable
            );
        },
        
        showHelp: function() {
            // Could trigger a modal showing all shortcuts
            console.log('Keyboard Shortcuts:', this.shortcuts);
        }
    };

    // ========================================
    // SEARCH FUNCTIONALITY
    // ========================================

    window.searchManager = {
        minChars: 2,
        debounceMs: 300,
        
        init: function(inputSelector, resultsSelector, searchFn) {
            const input = document.querySelector(inputSelector);
            const results = document.querySelector(resultsSelector);
            
            if (!input) return;
            
            const debouncedSearch = growITUtils.debounce(async (query) => {
                if (query.length < this.minChars) {
                    results?.classList.remove('open');
                    return;
                }
                
                results?.classList.add('loading');
                
                try {
                    const data = await searchFn(query);
                    this.renderResults(results, data);
                } catch (error) {
                    console.error('Search error:', error);
                }
                
                results?.classList.remove('loading');
            }, this.debounceMs);
            
            input.addEventListener('input', (e) => {
                debouncedSearch(e.target.value);
            });
            
            input.addEventListener('focus', () => {
                if (input.value.length >= this.minChars) {
                    results?.classList.add('open');
                }
            });
            
            document.addEventListener('click', (e) => {
                if (!e.target.closest('.search-container')) {
                    results?.classList.remove('open');
                }
            });
        },
        
        renderResults: function(container, data) {
            if (!container) return;
            
            if (!data || data.length === 0) {
                container.innerHTML = '<div class="search-empty">No results found</div>';
            } else {
                container.innerHTML = data.map(item => `
                    <a href="${item.url}" class="search-result-item">
                        <span class="search-result-title">${item.title}</span>
                        <span class="search-result-subtitle">${item.subtitle || ''}</span>
                    </a>
                `).join('');
            }
            
            container.classList.add('open');
        }
    };

    // ========================================
    // FORM UTILITIES
    // ========================================

    window.formUtils = {
        serialize: function(form) {
            const formData = new FormData(form);
            const data = {};
            
            for (const [key, value] of formData.entries()) {
                if (data[key]) {
                    if (!Array.isArray(data[key])) {
                        data[key] = [data[key]];
                    }
                    data[key].push(value);
                } else {
                    data[key] = value;
                }
            }
            
            return data;
        },
        
        validate: function(form) {
            const inputs = form.querySelectorAll('[required], [data-validate]');
            let isValid = true;
            
            inputs.forEach(input => {
                const error = this.validateInput(input);
                if (error) {
                    this.showError(input, error);
                    isValid = false;
                } else {
                    this.clearError(input);
                }
            });
            
            return isValid;
        },
        
        validateInput: function(input) {
            const value = input.value.trim();
            
            if (input.required && !value) {
                return 'This field is required';
            }
            
            if (input.type === 'email' && value) {
                const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
                if (!emailRegex.test(value)) {
                    return 'Please enter a valid email address';
                }
            }
            
            if (input.type === 'tel' && value) {
                const phoneRegex = /^[\d\s\-\(\)\+]+$/;
                if (!phoneRegex.test(value)) {
                    return 'Please enter a valid phone number';
                }
            }
            
            if (input.minLength && value.length < input.minLength) {
                return `Must be at least ${input.minLength} characters`;
            }
            
            if (input.maxLength && value.length > input.maxLength) {
                return `Must be no more than ${input.maxLength} characters`;
            }
            
            return null;
        },
        
        showError: function(input, message) {
            input.classList.add('is-invalid');
            
            let errorEl = input.parentElement.querySelector('.form-error');
            if (!errorEl) {
                errorEl = document.createElement('span');
                errorEl.className = 'form-error';
                input.parentElement.appendChild(errorEl);
            }
            errorEl.textContent = message;
        },
        
        clearError: function(input) {
            input.classList.remove('is-invalid');
            const errorEl = input.parentElement.querySelector('.form-error');
            errorEl?.remove();
        },
        
        clearAllErrors: function(form) {
            form.querySelectorAll('.is-invalid').forEach(input => {
                this.clearError(input);
            });
        }
    };

    // ========================================
    // DATA TABLE UTILITIES
    // ========================================

    window.tableUtils = {
        sort: function(tableId, columnIndex, direction = 'asc') {
            const table = document.getElementById(tableId);
            if (!table) return;
            
            const tbody = table.querySelector('tbody');
            const rows = Array.from(tbody.querySelectorAll('tr'));
            
            rows.sort((a, b) => {
                const aVal = a.cells[columnIndex].textContent.trim();
                const bVal = b.cells[columnIndex].textContent.trim();
                
                // Try numeric comparison
                const aNum = parseFloat(aVal.replace(/[^0-9.-]/g, ''));
                const bNum = parseFloat(bVal.replace(/[^0-9.-]/g, ''));
                
                if (!isNaN(aNum) && !isNaN(bNum)) {
                    return direction === 'asc' ? aNum - bNum : bNum - aNum;
                }
                
                // String comparison
                return direction === 'asc' 
                    ? aVal.localeCompare(bVal)
                    : bVal.localeCompare(aVal);
            });
            
            rows.forEach(row => tbody.appendChild(row));
        },
        
        filter: function(tableId, searchValue) {
            const table = document.getElementById(tableId);
            if (!table) return;
            
            const rows = table.querySelectorAll('tbody tr');
            const search = searchValue.toLowerCase();
            
            rows.forEach(row => {
                const text = row.textContent.toLowerCase();
                row.style.display = text.includes(search) ? '' : 'none';
            });
        },
        
        exportCSV: function(tableId, filename = 'export.csv') {
            const table = document.getElementById(tableId);
            if (!table) return;
            
            const rows = table.querySelectorAll('tr');
            const csv = [];
            
            rows.forEach(row => {
                const cols = row.querySelectorAll('td, th');
                const rowData = Array.from(cols).map(col => {
                    let text = col.textContent.trim();
                    // Escape quotes and wrap in quotes if needed
                    if (text.includes(',') || text.includes('"') || text.includes('\n')) {
                        text = '"' + text.replace(/"/g, '""') + '"';
                    }
                    return text;
                });
                csv.push(rowData.join(','));
            });
            
            const blob = new Blob([csv.join('\n')], { type: 'text/csv' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename;
            a.click();
            URL.revokeObjectURL(url);
        }
    };

    // ========================================
    // BLAZOR INTEROP
    // ========================================

    window.blazorInterop = {
        // Called from Blazor to show toast
        showToast: function(message, type, duration) {
            toastManager.show(message, type, duration);
        },
        
        // Called from Blazor to toggle theme
        toggleTheme: function() {
            return themeManager.toggle();
        },
        
        // Called from Blazor to get current theme
        getTheme: function() {
            return themeManager.getTheme();
        },
        
        // Called from Blazor to open modal
        openModal: function(modalId) {
            modalManager.open(modalId);
        },
        
        // Called from Blazor to close modal
        closeModal: function(modalId) {
            modalManager.close(modalId);
        },
        
        // Called from Blazor to copy text
        copyToClipboard: function(text) {
            return growITUtils.copyToClipboard(text);
        },
        
        // Called from Blazor to scroll to element
        scrollToElement: function(selector) {
            growITUtils.scrollToElement(selector);
        },
        
        // Called from Blazor for focus management
        focusElement: function(selector) {
            const element = document.querySelector(selector);
            element?.focus();
        },
        
        // Initialize Syncfusion dark mode
        syncfusionTheme: function(theme) {
            // Syncfusion theme switching if needed
            document.documentElement.setAttribute('data-theme', theme);
        }
    };

    // ========================================
    // FISCAL YEAR SELECTOR
    // ========================================

    window.fiscalYearManager = {
        STORAGE_KEY: 'growit-fiscal-year',
        
        get: function() {
            const saved = localStorage.getItem(this.STORAGE_KEY);
            if (saved) return saved;
            
            // Default to current fiscal year (July - June)
            const now = new Date();
            const month = now.getMonth();
            const year = now.getFullYear();
            
            // If before July, fiscal year started last year
            const fiscalStart = month < 6 ? year - 1 : year;
            return `FY${fiscalStart.toString().slice(-2)}-${(fiscalStart + 1).toString().slice(-2)}`;
        },
        
        set: function(fiscalYear) {
            localStorage.setItem(this.STORAGE_KEY, fiscalYear);
            window.dispatchEvent(new CustomEvent('fiscalYearChanged', { detail: { fiscalYear } }));
        },
        
        getDateRange: function(fiscalYear) {
            // Parse FY24-25 format
            const match = fiscalYear?.match(/FY(\d{2})-(\d{2})/);
            if (!match) return null;
            
            const startYear = 2000 + parseInt(match[1]);
            return {
                start: new Date(startYear, 6, 1), // July 1
                end: new Date(startYear + 1, 5, 30) // June 30
            };
        }
    };

    // ========================================
    // INITIALIZATION
    // ========================================

    function init() {
        themeManager.init();
        sidebarManager.init();
        dropdownManager.init();
        keyboardShortcuts.init();
        
        // Set up click handler for sidebar overlay
        const overlay = document.querySelector('.sidebar-overlay');
        overlay?.addEventListener('click', () => sidebarManager.closeMobile());
        
        // Set up theme toggle button
        document.querySelectorAll('.theme-toggle, [data-toggle-theme]').forEach(btn => {
            btn.addEventListener('click', () => themeManager.toggle());
        });
        
        // Set up sidebar toggle
        document.querySelectorAll('.sidebar-toggle, [data-toggle-sidebar]').forEach(btn => {
            btn.addEventListener('click', () => sidebarManager.toggle());
        });
        
        // Mobile menu button
        document.querySelectorAll('.mobile-menu-btn').forEach(btn => {
            btn.addEventListener('click', () => sidebarManager.openMobile());
        });
        
        console.log('GrowIT Application Initialized');
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // ========================================
    // BLAZOR INTEROP
    // Functions called from Blazor components
    // ========================================

    window.blazorInterop = {
        getTheme: function() {
            return themeManager.getTheme();
        },
        
        setTheme: function(theme) {
            themeManager.setTheme(theme);
            return theme;
        },
        
        toggleTheme: function() {
            return themeManager.toggle();
        },
        
        getSidebarState: function() {
            return sidebarManager.isExpanded();
        },
        
        toggleSidebar: function() {
            sidebarManager.toggle();
        },
        
        openMobileSidebar: function() {
            sidebarManager.openMobile();
        },
        
        closeMobileSidebar: function() {
            sidebarManager.closeMobile();
        },
        
        showToast: function(message, type, duration) {
            toastManager.show(message, type || 'info', duration || 3000);
        },
        
        copyToClipboard: async function(text) {
            return await growITUtils.copyToClipboard(text);
        },
        
        downloadFile: function(fileName, contentType, base64Content) {
            const link = document.createElement('a');
            link.download = fileName;
            link.href = `data:${contentType};base64,${base64Content}`;
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
        },
        
        printElement: function(elementId) {
            const element = document.getElementById(elementId);
            if (element) {
                const printWindow = window.open('', '_blank');
                printWindow.document.write(`
                    <html>
                        <head>
                            <title>Print</title>
                            <link rel="stylesheet" href="/css/app.css" />
                            <style>
                                body { padding: 20px; }
                                @media print { body { padding: 0; } }
                            </style>
                        </head>
                        <body>${element.innerHTML}</body>
                    </html>
                `);
                printWindow.document.close();
                printWindow.print();
            }
        },
        
        focusElement: function(selector) {
            const element = document.querySelector(selector);
            if (element) element.focus();
        },
        
        scrollToTop: function() {
            window.scrollTo({ top: 0, behavior: 'smooth' });
        },
        
        scrollToElement: function(selector, offset) {
            growITUtils.scrollToElement(selector, offset || 80);
        }
    };

})();
