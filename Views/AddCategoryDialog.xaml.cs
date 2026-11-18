using System;
using System.ComponentModel;
using System.Windows;
using DocuArchivePro.Models;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class AddCategoryDialog : Window, INotifyPropertyChanged
    {
        private readonly IDocumentService _documentService;
        private readonly Category? _existingCategory;
        private readonly PropertyChangedEventHandler _locHandler;

        public ILocalizationService Loc => LocalizationService.Instance;
        public Category? CreatedCategory { get; private set; }
        public bool IsEditMode => _existingCategory != null;

        public string DialogTitle => Loc[IsEditMode ? "EditCatWindowTitle" : "AddCatWindowTitle"];
        public string HeaderText => Loc[IsEditMode ? "EditCatHeader" : "AddCatHeader"];
        public string SubheaderText => Loc[IsEditMode ? "EditCatSubheader" : "AddCatSubheader"];
        public string SaveButtonText => Loc[IsEditMode ? "EditCatSaveBtn" : "AddCatSaveBtn"];
        public string HeaderIconPath => "pack://application:,,,/Resources/Icons/SecurityLockIcon.png";
        public Visibility EditLockVisibility => Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AddCategoryDialog(IDocumentService documentService, Category? existingCategory = null)
        {
            InitializeComponent();
            _documentService = documentService;
            _existingCategory = existingCategory;
            DataContext = this;

            if (_existingCategory != null)
            {
                TxtCategoryName.Text = _existingCategory.Name;
                TxtCategoryDescription.Text = _existingCategory.Description;
            }

            _locHandler = (s, e) =>
            {
                if (e.PropertyName == nameof(ILocalizationService.CurrentLanguage))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DialogTitle)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeaderText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubheaderText)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SaveButtonText)));
                }
            };
            Loc.PropertyChanged += _locHandler;
            Closed += (s, e) => Loc.PropertyChanged -= _locHandler;

            Loaded += (s, e) =>
            {
                TxtCategoryName.Focus();
                TxtCategoryName.SelectAll();
            };
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtCategoryName.Text.Trim();
            string desc = TxtCategoryDescription.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى إدخال اسم الإضبارة أو التصنيف." : "Please enter a dossier or category name.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCategoryName.Focus();
                return;
            }

            if (await _documentService.IsCategoryNameTakenAsync(name, _existingCategory?.Id ?? 0))
            {
                MessageBox.Show(
                    Loc.IsArabic
                        ? $"توجد إضبارة أخرى مسجلة بالاسم ({name}) بالفعل! يرجى اختيار اسم فريد للإضبارة."
                        : $"A dossier with the name ({name}) already exists! Please choose a unique name.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCategoryName.Focus();
                return;
            }

            try
            {
                if (_existingCategory != null)
                {
                    string actionTitle = Loc.IsArabic ? "تعديل الإضبارة / التصنيف" : "Edit Dossier / Category";
                    string warningMsg = Loc.IsArabic
                        ? $"هل أنت متأكد من رغبتك في تعديل بيانات هذه الإضبارة؟\n\nالاسم السابق: {_existingCategory.DisplayName}\nالاسم الجديد: {name}\n\nيتطلب تنفيذ التعديل إدخال كلمة السر المعدة مسبقاً."
                        : $"Are you sure you want to modify this dossier?\n\nPrevious Name: {_existingCategory.DisplayName}\nNew Name: {name}\n\nSaving changes requires the pre-configured security password.";

                    if (!ActionPinDialog.ConfirmWarningAndVerifyPin(actionTitle, warningMsg, this))
                    {
                        return;
                    }

                    _existingCategory.Name = name;
                    _existingCategory.Description = desc;
                    CreatedCategory = await _documentService.UpdateCategoryAsync(_existingCategory);
                }
                else
                {
                    string actionTitle = Loc.IsArabic ? "إضافة إضبارة / تصنيف جديد" : "Add New Dossier / Category";
                    string warningMsg = Loc.IsArabic
                        ? $"هل أنت متأكد من رغبتك في إضافة إضبارة جديدة للأرشيف؟\n\nاسم الإضبارة: {name}\n\nيتطلب إضافة إضبارة جديدة إدخال كلمة السر الخاصة بالحذف والتعديل المعدة مسبقاً."
                        : $"Are you sure you want to add this new archive dossier?\n\nDossier Name: {name}\n\nAdding a new dossier requires the pre-configured Edit/Delete security password.";

                    if (!ActionPinDialog.ConfirmWarningAndVerifyPin(actionTitle, warningMsg, this))
                    {
                        return;
                    }

                    var newCat = new Category
                    {
                        Name = name,
                        Description = desc,
                        IsActive = true
                    };

                    CreatedCategory = await _documentService.AddCategoryAsync(newCat);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"فشل حفظ الإضبارة:\n{ex.Message}" : $"Failed to save dossier:\n{ex.Message}",
                    Loc["Error"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
