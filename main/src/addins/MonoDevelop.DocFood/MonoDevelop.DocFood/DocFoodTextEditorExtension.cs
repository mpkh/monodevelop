// 
// TextEditorExtension.cs
//  =
// Author:
//       Mike Krüger <mkrueger@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using MonoDevelop.Ide.Gui.Content;
using System.Text;
using ICSharpCode.NRefactory.TypeSystem;
using MonoDevelop.Ide.TypeSystem;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.CSharp.TypeSystem;
using MonoDevelop.Ide.Editor;
using MonoDevelop.Ide.Editor.Extension;
using System.Linq;

namespace MonoDevelop.DocFood
{
	class DocFoodTextEditorExtension : TextEditorExtension
	{
		string GenerateDocumentation (IEntity member, string indent)
		{
			string doc = DocumentBufferHandler.GenerateDocumentation (Editor, member, indent);
			int trimStart = (Math.Min (doc.Length - 1, indent.Length + "//".Length));
			return doc.Substring (trimStart).TrimEnd ('\n', '\r');
		}
		
		string GenerateEmptyDocumentation (IEntity member, string indent)
		{
			string doc = DocumentBufferHandler.GenerateEmptyDocumentation (Editor, member, indent);
			int trimStart = (Math.Min (doc.Length - 1, indent.Length + "//".Length));
			return doc.Substring (trimStart).TrimEnd ('\n', '\r');
		}

		public override bool KeyPress (Gdk.Key key, char keyChar, Gdk.ModifierType modifier)
		{
			if (keyChar != '/')
				return base.KeyPress (key, keyChar, modifier);
			
			var line = Editor.GetLine (Editor.CaretLine);
			string text = Editor.GetTextAt (line.Offset, line.Length);
			
			if (!text.EndsWith ("//", StringComparison.Ordinal))
				return base.KeyPress (key, keyChar, modifier);

			// check if there is doc comment above or below.
			var l = line.PreviousLine;
			while (l != null && l.Length == 0)
				l = l.PreviousLine;
			if (l != null && Editor.GetTextAt (l).TrimStart ().StartsWith ("///", StringComparison.Ordinal))
				return base.KeyPress (key, keyChar, modifier);

			l = line.NextLine;
			while (l != null && l.Length == 0)
				l = l.NextLine;
			if (l != null && Editor.GetTextAt (l).TrimStart ().StartsWith ("///", StringComparison.Ordinal))
				return base.KeyPress (key, keyChar, modifier);

			var member = GetMemberToDocument ();
			if (member == null)
				return base.KeyPress (key, keyChar, modifier);
			
			string documentation = GenerateDocumentation (member, Editor.GetLineIndent (line));
			if (string.IsNullOrEmpty (documentation))
				return base.KeyPress (key, keyChar, modifier);
			
			string documentationEmpty = GenerateEmptyDocumentation (member, Editor.GetLineIndent (line));
			
			int offset = Editor.CaretOffset;
			
			int insertedLength;
			
			// Insert key (3rd undo step)
			Editor.Insert (offset, "/");
			using (var undo = Editor.OpenUndoGroup ()) {
				documentationEmpty = Editor.FormatString (offset, documentationEmpty); 
				insertedLength = documentationEmpty.Length;
				Editor.Replace (offset, 1, documentationEmpty);
				// important to set the caret position here for the undo step
				Editor.CaretOffset = offset + insertedLength;
			}
			
			using (var undo = Editor.OpenUndoGroup ()) {
				documentation = Editor.FormatString (offset, documentation); 
				Editor.Replace (offset, insertedLength, documentation);
				insertedLength = documentation.Length;
				if (SelectSummary (offset, insertedLength, documentation) == false)
					Editor.CaretOffset = offset + insertedLength;
			}
			return false;
		}

		/// <summary>
		/// Make the summary content selected
		/// </summary>
		/// <returns>
		/// <c>true</c>, if summary was selected, <c>false</c> if summary was not found.
		/// </returns>
		/// <param name='offset'>
		/// Offset in document where the documentation is inserted
		/// </param>
		/// <param name='insertedLength'>
		/// the length of the summary content.
		/// </param>
		/// <param name='documentation'>
		/// Documentation containing the summary
		/// </param>
		bool SelectSummary (int offset, int insertedLength, string documentation)
		{
			//Adjust the line endings to what the document uses to assure correct offset within the documentation
			if (insertedLength > documentation.Length)
				documentation = documentation.Replace ("\n", "\r\n");

			const string summaryStart = "<summary>";
			const string summaryEnd = "</summary>";
			int start = documentation.IndexOf (summaryStart, StringComparison.Ordinal);
			int end = documentation.IndexOf (summaryEnd, StringComparison.Ordinal);
			if (start < 0 || end < 0)
				return false;
			start += summaryStart.Length;
			string summaryText = documentation.Substring (start, end - start).Trim (new char[] {' ', '\t', '\r', '\n', '/'});
			start = documentation.IndexOf (summaryText, start, StringComparison.Ordinal);
			if (start < 0)
				return false;
			Editor.CaretOffset = offset + start;
			Editor.SetSelection (offset + start, offset + start + summaryText.Length);
			return true;
		}

		bool IsEmptyBetweenLines (int start, int end)
		{
			for (int i = start + 1; i < end - 1; i++) {
				var lineSegment = Editor.GetLine (i);
				if (lineSegment == null)
					break;
				if (lineSegment.Length != Editor.GetLineIndent (lineSegment).Length)
					return false;
				
			}
			return true;
		}	
		
		IEntity GetMemberToDocument ()
		{
			var parsedDocument = EditContext.ParsedDocument;
			
			var type = parsedDocument.GetInnermostTypeDefinition (Editor.CaretLocation);
			if (type == null) {
				foreach (var t in parsedDocument.TopLevelTypeDefinitions) {
					if (t.Region.BeginLine > Editor.CaretLine) {
						var ctx = (parsedDocument.ParsedFile as CSharpUnresolvedFile).GetTypeResolveContext (EditContext.Compilation, t.Region.Begin);
						return t.Resolve (ctx).GetDefinition ();
					}
				}
				return null;
			}
			
			IEntity result = null;
			foreach (var member in type.Members) {
				if (member.Region.Begin > new TextLocation (Editor.CaretLine, Editor.CaretColumn) && (result == null || member.Region.Begin < result.Region.Begin) && IsEmptyBetweenLines (Editor.CaretLine, member.Region.BeginLine)) {
					var ctx = (parsedDocument.ParsedFile as CSharpUnresolvedFile).GetTypeResolveContext (EditContext.Compilation, member.Region.Begin);
					result = member.CreateResolved (ctx);
				}
			}

			foreach (var member in type.NestedTypes) {
				if (member.Region.Begin > new TextLocation (Editor.CaretLine, Editor.CaretColumn) && (result == null || member.Region.Begin < result.Region.Begin) && IsEmptyBetweenLines (Editor.CaretLine, member.Region.BeginLine)) {
					var ctx = (parsedDocument.ParsedFile as CSharpUnresolvedFile).GetTypeResolveContext (EditContext.Compilation, member.Region.Begin);
					result = member.Resolve (ctx).GetDefinition ();
				}
			}
			return result;
		}
	}
}

