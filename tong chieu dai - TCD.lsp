(vl-load-com)

(defun c:TCD ( / ss i ent obj doc util objID propName fieldStr finalStr textHeight textLayer space pt0 mtextObj loop gr code pt pt-wcs)
  (setq doc (vla-get-activedocument (vlax-get-acad-object)))
  (setq util (vla-get-utility doc))
  
  (princ "\nQuet chon cac duong Line / Polyline / Arc roi rac can do tong chieu dai: ")
  
  (if (setq ss (ssget '((0 . "LINE,LWPOLYLINE,POLYLINE,ARC"))))
    (progn
      (vla-StartUndoMark doc)
      
      ;; 1. Ghep chuoi cong thuc Field tinh tong chieu dai
      (setq fieldStr "%<\\AcExpr ((")
      (setq i 0)
      (while (< i (sslength ss))
        (setq ent (ssname ss i))
        (setq obj (vlax-ename->vla-object ent))
        (setq objID (vlax-invoke-method util 'GetObjectIdString obj :vlax-false))
        
        (if (= (vla-get-ObjectName obj) "AcDbArc")
          (setq propName "ArcLength")
          (setq propName "Length")
        )
        
        (if (> i 0)
          (setq fieldStr (strcat fieldStr " + "))
        )
        
        (setq fieldStr (strcat fieldStr "%<\\AcObjProp Object(%<\\_ObjId " objID ">%)." propName ">%"))
        (setq i (1+ i))
      )
      
      ;; Quy doi mm -> m (* 0.001) va lay 2 chu so thap phan (%pr2)
      (setq fieldStr (strcat fieldStr ") * 0.001) \\f \"%lu2%pr2\">%"))
      (setq finalStr (strcat "H=" fieldStr "m"))
      
      ;; 2. Kiem tra khong gian Model / Layout
      (if (= (vla-get-activespace doc) 1)
        (setq space (vla-get-modelspace doc))
        (if (= (vla-get-mspace doc) :vlax-true)
          (setq space (vla-get-modelspace doc))
          (setq space (vla-get-paperspace doc))
        )
      )
      
      ;; 3. Cai dat Chieu cao chu = 500 va Layer = DB-A-Text (theo lenh A16)
      (setq textHeight 500.0)
      (setq textLayer "DB-A-Text")
      
      ;; Neu ban ve chua co layer DB-A-Text thi tu tao voi mau so 5 chuan cong ty
      (if (not (tblsearch "LAYER" textLayer))
        (command "_.-layer" "_m" textLayer "_c" "5" "" "")
      )
      
      ;; 4. Khoi tao MText ngay tai tam man hinh de bat dau keo Preview
      (setq pt0 (trans (getvar "VIEWCTR") 1 0))
      (setq mtextObj (vla-AddMText space (vlax-3d-point pt0) 0 finalStr))
      (vla-put-Height mtextObj textHeight)
      (vla-put-Layer mtextObj textLayer)
      
      ;; Ep diem dat ve Duoi cung ben trai (Bottom-Left = 7)
      (vla-put-AttachmentPoint mtextObj 7)
      (vla-put-InsertionPoint mtextObj (vlax-3d-point pt0))
      
      ;; Bat che do Highlight (net dut mo nhu lenh Copy/Move)
      (vla-Highlight mtextObj :vlax-true)
      (vla-Update mtextObj)
      
      (princ "\nDi chuyen chuot va Click chon vi tri dat Text: ")
      
      ;; 5. Vong lap di chuyen Preview theo con tro chuot
      (setq loop T)
      (while loop
        (setq gr (grread T 15 0)
              code (car gr)
              pt (cadr gr)
        )
        (cond
          ;; Khi di chuyen chuot (code = 5) -> Cap nhat vi tri Bottom-Left ngay tai mui ten chuot
          ((= code 5)
            (setq pt-wcs (trans pt 1 0))
            (vla-put-InsertionPoint mtextObj (vlax-3d-point pt-wcs))
            (vla-Highlight mtextObj :vlax-true)
            (vla-Update mtextObj)
          )
          ;; Khi Click chuot trai (code = 3) -> Chot vi tri dat Text
          ((= code 3)
            (setq pt-wcs (trans pt 1 0))
            (vla-put-InsertionPoint mtextObj (vlax-3d-point pt-wcs))
            (vla-Highlight mtextObj :vlax-false)
            (vla-Update mtextObj)
            (setq loop nil)
            (princ "\n>> Da dat Text thanh cong!")
          )
          ;; Khi nhan Enter / Space / Chuot phai -> Dat tai vi tri hien tai, hoac ESC -> Huy
          ((or (= code 11) (= code 25) (and (= code 2) (member pt '(13 32))))
            (vla-Highlight mtextObj :vlax-false)
            (vla-Update mtextObj)
            (setq loop nil)
            (princ "\n>> Da dat Text thanh cong!")
          )
          ((and (= code 2) (= pt 27))
            (vla-Delete mtextObj)
            (setq loop nil)
            (princ "\n*Da huy lenh*")
          )
        )
      )
      (vla-EndUndoMark doc)
    )
    (princ "\nKhong co doi tuong nao duoc chon!")
  )
  (princ)
)