﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static RetrieveTheFolder.WindowAPI;

namespace RetrieveTheFolder
{
    public partial class FormRetrieve : Form
    {
        public delegate void UpdateProgressBarDelegate();
        public delegate void DgvDelegate();
        public delegate void ProgressBarDelegate();
        public delegate void ButtonDelegate();
        private object objLock = new object();
        /// <summary>
        /// 原始数据
        /// </summary>
        private List<RowInfo> data = new List<RowInfo>();
        /// <summary>
        /// 显示数据
        /// </summary>
        private BindingList<RowInfo> result = new BindingList<RowInfo>();
        public FormRetrieve()
        {
            InitializeComponent();
            dgvData.AutoGenerateColumns = false;

            this.Icon = RetrieveTheFolder.Properties.Resources.favicon;
        }

        private void btnRetrieve_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(this.txbFolder.Text) || !this.btnRetrieve.Enabled)
                return;
            this.btnRetrieve.Enabled = false;
            progressBar1.Show();
            progressBar1.Value = 1;
            this.LoadData();
        }

        public void LoadData()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new UpdateProgressBarDelegate(LoadData));
            }
            else
            {
                Thread thread = new Thread(new ThreadStart(() =>
                {
                    lock (objLock)
                    {
                        Task task = Task.Factory.StartNew(() =>
                        {
                            EnumerateFiles(this.txbFolder.Text);
                        });
                        task.Wait();

                        if (dgvData.InvokeRequired)
                        {
                            this.dgvData.Invoke(new DgvDelegate(LoadDgv));
                        }
                        else
                        {
                            this.LoadDgv();
                        }

                        if (progressBar1.InvokeRequired)
                        {
                            this.progressBar1.Invoke(new ProgressBarDelegate(SetProgressEnable));
                        }
                        else
                        {
                            this.SetProgressEnable();
                        }

                        if (btnRetrieve.InvokeRequired)
                        {
                            this.btnRetrieve.Invoke(new ButtonDelegate(SetButtonEnable));
                        }
                        else
                        {
                            this.SetButtonEnable();
                        }
                    }

                }));
                thread.IsBackground = true;
                thread.Start();
            }
        }

        private void LoadDgv()
        {
            if (nudValue.Value > 0)
            {
                var minValue = nudValue.Value * 1024 * 1024 * 1024;
                data = data.Where(w => w.Size >= ((long)minValue)).ToList();
            }

            if (ckbGroupFolder.Checked)
            {
                var firstFolder = data.Where(s => s.DirectoryPath.Split(new string[] { "\\" }, StringSplitOptions.RemoveEmptyEntries).Length == 2).Select(w => w.DirectoryPath).Distinct().ToList();
                var dataDic = new Dictionary<string, List<RowInfo>>();
                var firstFolderDic = new Dictionary<string, RowInfo>();
                firstFolder.ForEach(key =>
                {
                    firstFolderDic.Add(key, data.FirstOrDefault(f => f.DirectoryPath.Equals(key)));
                    dataDic.Add(key, data.Where(w => w.DirectoryPath.StartsWith(key)).OrderByDescending(o => o.Size).ToList());
                });
                var temp = new List<RowInfo>();
                firstFolderDic.OrderByDescending(o => o.Value.Size).Select(s => s.Key).ToList().ForEach(f =>
                {
                    temp.AddRange(dataDic[f]);
                });
                result = new BindingList<RowInfo>(temp);
            }
            else {
                result = new BindingList<RowInfo>(data.OrderByDescending(o=>o.Size).ThenBy(t=>t.DirectoryPath).ToList());
            }
            dgvData.DataSource = null;
            dgvData.DataSource = result;
        }

        private void SetButtonEnable()
        {
            this.btnRetrieve.Enabled = true;
        }
        private void SetProgressEnable()
        {
            this.progressBar1.Visible = false;
        }

        private void btnSelect_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
            {
                this.txbFolder.Text = folderBrowserDialog1.SelectedPath;
            }
        }


        private long EnumerateFiles(string folderPath)
        {
            WIN32_FIND_DATA findData;
            IntPtr findHandle = FindFirstFile(folderPath + @"\*", out findData);

            if (findHandle != IntPtr.Zero)
            {
                long sizeTotal = 0;
                do
                {
                    // 跳过"."和".."这两个特殊目录
                    if (string.IsNullOrEmpty(findData.cFileName) || findData.cFileName == "." || findData.cFileName == "..")
                    {
                        continue;
                    }

                    string fullPath = Path.Combine(folderPath, findData.cFileName);
                    if ((findData.dwFileAttributes & (uint)FileAttributes.Directory) != 0)
                    {
                        // 处理目录
                        Console.WriteLine($"Directory: {fullPath}");
                        var result = EnumerateFiles(fullPath);
                        sizeTotal += result;
                        if (result == 0)
                            continue;
                        data.Add(new RowInfo() { DirectoryName = findData.cFileName, Size = result, DirectoryPath = fullPath, Level = fullPath.Split(new string[] { "\\" }, StringSplitOptions.RemoveEmptyEntries).Length - 1 });
                    }
                    else
                    {
                        var size = (((long)findData.nFileSizeHigh << 32) | findData.nFileSizeLow);
                        // 处理文件
                        Console.WriteLine($"File: {fullPath} - Size: {size} bytes");
                        sizeTotal += size;
                    }

                }
                while (FindNextFile(findHandle, out findData));
                FindClose(findHandle);
                return sizeTotal;
            }
            return 0;
        }

        private void FormRetrieve_Shown(object sender, EventArgs e)
        {
            this.progressBar1.Visible = false;
        }

        private void dgvData_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
        }

        private void dgvData_CellMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex != 3)
                return;
            var pathValue = this.dgvData.Rows[e.RowIndex].Cells[e.ColumnIndex].Value.ToString();
            Process.Start(pathValue);
        }

        private void dgvData_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
            var row = this.dgvData.Rows[e.RowIndex];
            var rowData = (RowInfo)row.DataBoundItem;
            if (rowData == null)
                return;
            switch (rowData.Level)
            {
                case 1:
                    row.DefaultCellStyle.BackColor = Color.LightSkyBlue;
                    break;
                case 2:
                    row.DefaultCellStyle.BackColor = Color.LightYellow;
                    break;
                default:
                    row.DefaultCellStyle.BackColor = Color.White;
                    break;
            }
        }

        private void btnFilter_Click(object sender, EventArgs e)
        {
            LoadDgv();
        }
    }

    public class RowInfo
    {
        private const double MSize = 1024.0 * 1024.0;
        private const double GSize = 1024.0 * 1024.0 * 1024.0;
        public string DirectoryName { get; set; }
        // 在计算时就保留两位小数
        public string SizeM { get { return (this.Size / MSize).ToString("0.00M"); } }
        /// <summary>
        /// 原始值
        /// </summary>
        public double Size { get; set; }

        public string SizeG { get { return (this.Size / GSize).ToString("0.00G"); } }
        public string DirectoryPath { get; set; }

        public int Level { get; set; }
    }
}
