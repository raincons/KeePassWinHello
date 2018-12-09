﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using KeePass.Forms;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;

namespace WinHelloQuickUnlock
{
    class KeyManager
    {
        private readonly KeyCipher _keyCipher;
        private readonly KeyStorage _keyStorage;

        public KeyManager(IntPtr windowHandle)
        {
            _keyStorage = new KeyStorage();
            _keyCipher = new KeyCipher(Settings.ConfirmationMessage, windowHandle);
        }

        public void OnKeyPrompt(KeyPromptForm keyPromptForm)
        {
            if (keyPromptForm.SecureDesktopMode)
                return;

            ProtectedKey encryptedData;
            var dbPath = GetDbPath(keyPromptForm);
            if (!FindQuickUnlockData(dbPath, out encryptedData))
                return;

            CompositeKey compositeKey;
            if (TryGetCompositeKey(encryptedData, out compositeKey))
            {
                SetCompositeKey(keyPromptForm, compositeKey);
                // Remove flushing
                keyPromptForm.Visible = false;
                keyPromptForm.Opacity = 0;

                keyPromptForm.DialogResult = DialogResult.OK;
                keyPromptForm.Close();
            }
        }

        public void OnDBClosing(object sender, FileClosingEventArgs e)
        {
            if (e == null)
            {
                Debug.Fail("Event is null");
                return;
            }

            if (e.Cancel || e.Database == null || e.Database.MasterKey == null || e.Database.IOConnectionInfo == null)
                return;

            string dbPath = e.Database.IOConnectionInfo.Path;
            if (!IsDBLocking(e))
            {
                _keyStorage.Remove(dbPath);
            }
            else if (WinHelloCryptProvider.IsAvailable())
            {
                _keyStorage.AddOrUpdate(dbPath, ProtectedKey.Create(e.Database.MasterKey, _keyCipher));
            }
        }

        private static bool IsDBLocking(FileClosingEventArgs e)
        {
            try
            {
                var FlagsProperty = typeof(FileClosingEventArgs).GetProperty("Flags");
                if (FlagsProperty == null)
                    return true;

                var FlagsType = FlagsProperty.PropertyType;
                int FlagsValue = Convert.ToInt32(FlagsProperty.GetValue(e, null));

                var names = Enum.GetNames(FlagsType);
                for (int i = 0; i != names.Length; ++i)
                {
                    if (names[i] == "Locking")
                    {
                        int Locking = Convert.ToInt32(Enum.GetValues(FlagsType).GetValue(i));
                        if ((FlagsValue & Locking) != Locking)
                        {
                            return false;
                        }
                        break;
                    }
                }
            }
            catch { }
            return true;
        }

        private void SetCompositeKey(KeyPromptForm keyPromptForm, CompositeKey compositeKey)
        {
            var fieldInfo = keyPromptForm.GetType().GetField("m_pKey", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fieldInfo != null)
                fieldInfo.SetValue(keyPromptForm, compositeKey);
        }

        private bool TryGetCompositeKey(ProtectedKey encryptedData, out CompositeKey compositeKey)
        {
            try
            {
                compositeKey = encryptedData.GetCompositeKey(_keyCipher);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Fail(ex.ToString()); // TODO: fix canceled exception
                compositeKey = null;
                return false;
            }
        }

        private bool FindQuickUnlockData(string dbPath, out ProtectedKey encryptedData)
        {
            if (String.IsNullOrEmpty(dbPath))
            {
                encryptedData = null;
                return false;
            }

            return _keyStorage.TryGetValue(dbPath, out encryptedData);
        }

        private static string GetDbPath(KeyPromptForm keyPromptForm)
        {
            var ioInfo = GetIoInfo(keyPromptForm);
            if (ioInfo == null)
                return null;
            return ioInfo.Path;
        }

        private static IOConnectionInfo GetIoInfo(KeyPromptForm keyPromptForm)
        {
            var fieldInfo = keyPromptForm.GetType().GetField("m_ioInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fieldInfo == null)
                return null;
            return fieldInfo.GetValue(keyPromptForm) as IOConnectionInfo;
        }
    }
}
