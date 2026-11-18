using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DocuArchivePro.Models;
using DocuArchivePro.Services;

namespace DocuArchivePro.Views
{
    public partial class AddEntityDialog : Window, INotifyPropertyChanged
    {
        private readonly IDocumentService _documentService;
        private readonly CorrespondentEntity? _existingEntity;
        private readonly PropertyChangedEventHandler _locHandler;

        public ILocalizationService Loc => LocalizationService.Instance;
        public CorrespondentEntity? CreatedEntity { get; private set; }
        public bool IsEditMode => _existingEntity != null;

        public string DialogTitle => Loc[IsEditMode ? "EditEntityWindowTitle" : "AddEntityWindowTitle"];
        public string HeaderText => Loc[IsEditMode ? "EditEntityHeader" : "AddEntityHeader"];
        public string SubheaderText => Loc["AddEntitySubheader"];
        public string SaveButtonText => Loc["AddEntitySaveBtn"];
        public string HeaderIconPath => (SecurityService.Instance.CurrentUser.IsAdmin || SecurityService.Instance.CurrentUser.CanManageEntities)
            ? "pack://application:,,,/Resources/Icons/AppIcon.png"
            : "pack://application:,,,/Resources/Icons/SecurityLockIcon.png";

        public event PropertyChangedEventHandler? PropertyChanged;

        public AddEntityDialog(
            IDocumentService documentService,
            CorrespondentEntity? existingEntity = null,
            string? initialName = null,
            string? defaultEntityType = null)
        {
            InitializeComponent();
            _documentService = documentService;
            _existingEntity = existingEntity;
            DataContext = this;

            if (_existingEntity != null)
            {
                TxtEntityName.Text = _existingEntity.Name;
                TxtEntityNotes.Text = _existingEntity.Notes;
                SelectEntityTypeTag(_existingEntity.EntityType);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(initialName))
                {
                    TxtEntityName.Text = initialName.Trim();
                }
                if (!string.IsNullOrWhiteSpace(defaultEntityType))
                {
                    SelectEntityTypeTag(defaultEntityType);
                }
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
                TxtEntityName.Focus();
                TxtEntityName.SelectAll();
            };
        }

        private void SelectEntityTypeTag(string entityType)
        {
            for (int i = 0; i < CmbEntityType.Items.Count; i++)
            {
                if (CmbEntityType.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), entityType, StringComparison.OrdinalIgnoreCase))
                {
                    CmbEntityType.SelectedIndex = i;
                    return;
                }
            }
            CmbEntityType.SelectedIndex = 0;
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtEntityName.Text.Trim();
            string notes = TxtEntityNotes.Text.Trim();
            string entityType = (CmbEntityType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Both";

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(
                    Loc.IsArabic ? "يرجى إدخال اسم جهة الصادر أو الوارد." : "Please enter the correspondent entity name.",
                    Loc["Warning"], MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtEntityName.Focus();
                return;
            }

            try
            {
                if (_existingEntity != null)
                {
                    _existingEntity.Name = name;
                    _existingEntity.EntityType = entityType;
                    _existingEntity.Notes = notes;
                    CreatedEntity = await _documentService.UpdateCorrespondentEntityAsync(_existingEntity);
                }
                else
                {
                    var newEntity = new CorrespondentEntity
                    {
                        Name = name,
                        EntityType = entityType,
                        Notes = notes,
                        IsActive = true
                    };
                    CreatedEntity = await _documentService.AddCorrespondentEntityAsync(newEntity);
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Loc.IsArabic ? $"فشل حفظ الجهة:\n{ex.Message}" : $"Failed to save entity:\n{ex.Message}",
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
